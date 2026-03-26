# Deploying Muxity to Azure Container Apps

## Prerequisites

- Azure subscription (free tier works for preview)
- [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
- [CloudAMQP account](https://www.cloudamqp.com) — free Little Lemur plan for RabbitMQ
- GitHub account (images pushed to GHCR, Blazor WASM to Static Web Apps)

---

## One-shot deploy (first time)

```bash
az login
bash infra/azure/deploy.sh
```

You'll be prompted for:
- **RabbitMQ connection string** — from CloudAMQP → your instance → AMQP URL
- **JWT signing key** — auto-generated if omitted
- **GitHub PAT** — create at github.com/settings/tokens with `repo` scope

---

## GitHub Actions (CI/CD after first deploy)

Add these **secrets** to your repo (`Settings → Secrets and variables → Actions`):

| Secret | Value |
|--------|-------|
| `AZURE_CREDENTIALS` | Output of `az ad sp create-for-rbac --sdk-auth` |
| `RABBITMQ_CONNECTION_STRING` | CloudAMQP AMQPS URL |
| `JWT_SIGNING_KEY` | 32+ random chars |
| `GH_PAT_STATIC_WEB_APP` | GitHub PAT (repo scope) |

Add these **variables**:

| Variable | Example |
|----------|---------|
| `AZURE_RESOURCE_GROUP` | `muxity-preview` |
| `AZURE_LOCATION` | `eastus` |
| `AZURE_PREFIX` | `muxity` |
| `GOOGLE_CLIENT_ID` | *(optional)* |
| `MICROSOFT_CLIENT_ID` | *(optional)* |

Then push to `main` — the workflow builds images, deploys Bicep, and publishes the Blazor WASM automatically.

---

## Architecture

```
GitHub Actions
  ├── build-images → ghcr.io/rfsprog/muxity-{api,streaming,transcoder}:sha
  ├── deploy-infra → az deployment group create (Bicep)
  └── deploy-web   → Azure Static Web Apps (Blazor WASM)

Azure Resources (all in one resource group)
  ├── Container Apps Environment
  │   ├── muxity-api          (0–5 replicas, scales to zero)
  │   ├── muxity-streaming    (0–10 replicas, scales to zero)
  │   └── muxity-transcoder   (0–3 replicas, KEDA RabbitMQ scaler)
  ├── Azure Static Web App    (Blazor WASM, free tier)
  ├── Azure Cosmos DB         (MongoDB API, free tier 1000 RU/s)
  ├── Azure Storage Account
  │   └── Azure Files share   (mounted to all 3 container apps at /data/storage)
  └── Log Analytics workspace
```

## Estimated cost (sandbox, light usage)

| Resource | Free allowance | Overage |
|----------|---------------|---------|
| Container Apps | 180k vCPU-s + 360k GiB-s/month | ~$0.000024/vCPU-s |
| Static Web App | Always free | — |
| Cosmos DB | 1000 RU/s, 25GB (free tier) | — |
| Azure Files | — | ~$0.06/GB/month |
| Log Analytics | 5GB/month free | $2.30/GB |

**Estimated total for a preview with light traffic: ~$0–5/month**

## Tear down

```bash
az group delete --name muxity-preview --yes
```
