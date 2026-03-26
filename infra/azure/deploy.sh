#!/usr/bin/env bash
# deploy.sh — One-shot provisioning of Muxity on Azure Container Apps
# Usage: bash infra/azure/deploy.sh
#
# Prerequisites:
#   az login
#   az extension add --name containerapp
#   docker (for local image builds, optional if using GitHub Actions)
#
# Required environment variables (or you'll be prompted):
#   RABBITMQ_CONNECTION_STRING  — amqps://user:pass@host/vhost  (CloudAMQP free tier)
#   JWT_SIGNING_KEY             — random 32+ char string
#   GH_PAT                      — GitHub PAT with repo scope (for Static Web Apps)
#   GOOGLE_CLIENT_ID            — (optional) Google OAuth client ID
#   MICROSOFT_CLIENT_ID         — (optional) Microsoft OAuth client ID

set -euo pipefail

# ── Config (override via env vars) ───────────────────────────────────────────
RESOURCE_GROUP="${RESOURCE_GROUP:-muxity-preview}"
LOCATION="${LOCATION:-eastus}"
PREFIX="${PREFIX:-muxity}"
REPO_URL="${REPO_URL:-https://github.com/rfsprog/Muxity}"
BRANCH="${BRANCH:-main}"
GHCR_OWNER="${GHCR_OWNER:-rfsprog}"
IMAGE_TAG="${IMAGE_TAG:-latest}"

# ── Prompt for secrets if not set ────────────────────────────────────────────
if [[ -z "${RABBITMQ_CONNECTION_STRING:-}" ]]; then
  echo ""
  echo "CloudAMQP free tier: https://www.cloudamqp.com"
  read -rsp "RabbitMQ connection string (amqps://...): " RABBITMQ_CONNECTION_STRING
  echo ""
fi

if [[ -z "${JWT_SIGNING_KEY:-}" ]]; then
  JWT_SIGNING_KEY=$(openssl rand -hex 32)
  echo "Generated JWT signing key: $JWT_SIGNING_KEY"
  echo "(save this — you'll need it if you redeploy)"
fi

if [[ -z "${GH_PAT:-}" ]]; then
  echo ""
  echo "GitHub PAT needed for Static Web Apps deployment."
  echo "Create one at: https://github.com/settings/tokens (repo scope)"
  read -rsp "GitHub PAT: " GH_PAT
  echo ""
fi

GOOGLE_CLIENT_ID="${GOOGLE_CLIENT_ID:-}"
MICROSOFT_CLIENT_ID="${MICROSOFT_CLIENT_ID:-}"

# ── Azure setup ───────────────────────────────────────────────────────────────
echo ""
echo "==> Creating resource group: $RESOURCE_GROUP ($LOCATION)"
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

echo "==> Registering Container Apps extension"
az extension add --name containerapp --upgrade --output none 2>/dev/null || true
az provider register --namespace Microsoft.App --wait --output none 2>/dev/null || true
az provider register --namespace Microsoft.OperationalInsights --wait --output none 2>/dev/null || true

# ── Deploy Bicep ──────────────────────────────────────────────────────────────
echo "==> Deploying Bicep template (this takes ~5 minutes)..."

DEPLOY_OUTPUT=$(az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file "$(dirname "$0")/main.bicep" \
  --parameters \
    "prefix=$PREFIX" \
    "location=$LOCATION" \
    "repositoryUrl=$REPO_URL" \
    "branch=$BRANCH" \
    "repositoryToken=$GH_PAT" \
    "rabbitMqConnectionString=$RABBITMQ_CONNECTION_STRING" \
    "jwtSigningKey=$JWT_SIGNING_KEY" \
    "googleClientId=$GOOGLE_CLIENT_ID" \
    "microsoftClientId=$MICROSOFT_CLIENT_ID" \
    "imageTag=$IMAGE_TAG" \
    "ghcrOwner=$GHCR_OWNER" \
  --output json)

API_URL=$(echo "$DEPLOY_OUTPUT" | jq -r '.properties.outputs.apiUrl.value')
STREAMING_URL=$(echo "$DEPLOY_OUTPUT" | jq -r '.properties.outputs.streamingUrl.value')
WEB_URL=$(echo "$DEPLOY_OUTPUT" | jq -r '.properties.outputs.webUrl.value')

# ── Print summary ─────────────────────────────────────────────────────────────
echo ""
echo "============================================================"
echo "  Muxity deployed successfully!"
echo "============================================================"
echo ""
echo "  Web:       https://$WEB_URL"
echo "  API:       $API_URL"
echo "  Streaming: $STREAMING_URL"
echo ""
echo "Next steps:"
echo "  1. Push container images to ghcr.io/$GHCR_OWNER/muxity-{api,streaming,transcoder}:$IMAGE_TAG"
echo "     (GitHub Actions does this automatically on push to main)"
echo "  2. Configure GitHub Actions secrets — see DEPLOY.md"
echo "  3. Set GOOGLE_CLIENT_ID / MICROSOFT_CLIENT_ID and re-run to enable OIDC login"
echo ""
echo "To tear down:"
echo "  az group delete --name $RESOURCE_GROUP --yes"
echo ""
