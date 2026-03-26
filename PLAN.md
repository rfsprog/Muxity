# Muxity — Phased Implementation Plan

## System Overview

Muxity is a distributed online video platform supporting upload, distributed hardware-accelerated transcoding, HLS streaming via CDN, and multi-client playback at scale (10,000+ videos, 50,000+ concurrent viewers).

---

## Architecture Summary

```
┌─────────────────────────────────────────────────────────────────────┐
│                          Clients (Blazor)                           │
└────────────────────────────┬────────────────────────────────────────┘
                             │
              ┌──────────────┴──────────────┐
              │                             │
     ┌────────▼────────┐          ┌─────────▼────────┐
     │   Main API       │          │  Streaming API    │
     │ (FastEndpoints)  │          │ (FastEndpoints)   │
     │  Auth, Upload,   │          │  Streaming Keys,  │
     │  Video Mgmt      │          │  HLS → CDN Redir  │
     └────────┬─────────┘          └──────────────────┘
              │
     ┌────────▼──────────────────────────────┐
     │            MongoDB                     │
     │  Users, Videos, Jobs, StreamingKeys    │
     └────────┬──────────────────────────────┘
              │
     ┌────────▼──────────────────────────────┐
     │        Message Queue (RabbitMQ)        │
     │    Transcoding job dispatch/results    │
     └────────┬──────────────────────────────┘
              │
     ┌────────▼──────────────────────────────┐
     │      Transcoding Worker Nodes          │
     │  FFmpeg + Intel QSV / NVIDIA CUDA      │
     │  HLS segment output → Storage          │
     └────────┬──────────────────────────────┘
              │
     ┌────────▼──────────────────────────────┐
     │            Storage Layer               │
     │    Local Filesystem  OR  S3 Bucket     │
     └────────┬──────────────────────────────┘
              │
     ┌────────▼──────────────────────────────┐
     │               CDN                      │
     │   Serves HLS segments to clients       │
     └───────────────────────────────────────┘
```

---

## Solution Structure

```
Muxity/
├── src/
│   ├── Muxity.Api/               # Main API — auth, upload, video management
│   ├── Muxity.Streaming/         # Streaming API — keys, HLS delivery, CDN redirect
│   ├── Muxity.Transcoder/        # Worker nodes — FFmpeg transcoding jobs
│   ├── Muxity.Web/               # Blazor frontend
│   └── Muxity.Shared/            # Shared models, DTOs, interfaces, constants
├── infra/
│   ├── docker-compose.yml
│   ├── docker-compose.override.yml
│   └── helm/
│       └── muxity/               # Helm chart
├── PLAN.md
└── README.md
```

---

## Phase 1 — Foundation

**Goal:** Runnable solution skeleton with auth and core data models.

### 1.1 Solution & Projects
- [ ] Create .NET 10 solution with projects: `Api`, `Streaming`, `Transcoder`, `Web`, `Shared`
- [ ] Configure `FastEndpoints` in `Api` and `Streaming`
- [ ] Configure MongoDB driver in `Api`, `Streaming`, `Transcoder`
- [ ] Shared library: domain models, DTOs, MongoDB collection names, constants

### 1.2 Domain Models (`Muxity.Shared`)
- [ ] `User` — Id, ExternalId (provider sub), Provider (Google/Microsoft), Email, DisplayName, CreatedAt
- [ ] `Video` — Id, OwnerId, Title, Description, Status (Pending/Transcoding/Ready/Failed), Visibility (Public/Private), StoragePath, StreamingKey, ThumbnailPath, DurationSeconds, CreatedAt, UpdatedAt
- [ ] `TranscodeJob` — Id, VideoId, Status, Progress, WorkerNodeId, HardwareAccel (QSV/CUDA/Software), CreatedAt, StartedAt, CompletedAt, Error
- [ ] `StreamingKey` — Id, VideoId, Key (GUID token), ExpiresAt (nullable for permanent), CreatedAt

### 1.3 Auth — Federated OIDC (`Muxity.Api`)
- [ ] OIDC middleware for Google and Microsoft identity providers
- [ ] JWT bearer token issuance after successful OIDC callback (short-lived access + refresh token stored in MongoDB)
- [ ] `POST /auth/callback` — exchange OIDC code, upsert user, return JWT
- [ ] `POST /auth/refresh` — rotate refresh token
- [ ] `GET /auth/me` — return current user profile

### 1.4 MongoDB Setup
- [ ] `MongoDbContext` with typed `IMongoCollection<T>` accessors
- [ ] Index definitions: `Videos` by OwnerId, Status; `TranscodeJobs` by VideoId, Status; `StreamingKeys` by Key
- [ ] Database seeding/migration helper (idempotent index creation on startup)

**Deliverable:** Auth flow works end-to-end with Google and Microsoft. JWT issued and validated.

---

## Phase 2 — Upload & Storage

**Goal:** Users can upload video files; raw files land in storage; records created in MongoDB.

### 2.1 Storage Abstraction (`Muxity.Shared`)
- [ ] `IStorageProvider` interface: `UploadAsync`, `DownloadStreamAsync`, `GetPublicUrlAsync`, `DeleteAsync`, `ExistsAsync`
- [ ] `LocalStorageProvider` — writes to a configurable base directory (streaming-directory mount)
- [ ] `S3StorageProvider` — wraps AWSSDK.S3 / MinIO-compatible endpoint
- [ ] Registration via config: `Storage:Provider = Local | S3`; S3 settings: endpoint, bucket, region, credentials

### 2.2 Upload Endpoint (`Muxity.Api`)
- [ ] `POST /videos/upload` — multipart form: file + metadata (title, description, visibility)
  - Validate file type (video/mp4, video/mkv, video/mov, video/webm)
  - Max file size configurable (default 10 GB, streamed — no buffering in memory)
  - Save raw file to storage under `raw/{videoId}/{filename}`
  - Create `Video` document (Status = Pending)
  - Publish `TranscodeJob` message to RabbitMQ queue
  - Return `videoId` + upload confirmation
- [ ] `GET /videos/{id}/upload-status` — returns Video status + progress from latest TranscodeJob

### 2.3 Video Management Endpoints (`Muxity.Api`)
- [ ] `GET /videos` — list own videos (paged, sortable, filterable by status/visibility)
- [ ] `GET /videos/{id}` — get single video (owner or public)
- [ ] `PATCH /videos/{id}` — update title, description, visibility
- [ ] `DELETE /videos/{id}` — soft-delete (Status = Deleted), removes streaming key, schedules storage cleanup
- [ ] `GET /videos/search?q=` — full-text search on title/description (MongoDB text index)

**Deliverable:** Upload flow complete. Files stored. Video documents in MongoDB. Job enqueued.

---

## Phase 3 — Distributed Transcoding

**Goal:** Worker nodes pick up jobs, transcode via FFmpeg with hardware acceleration, output HLS segments.

### 3.1 RabbitMQ Integration
- [ ] `TranscodeJobMessage`: VideoId, RawStoragePath, OutputBasePath, RequestedQualities
- [ ] Publisher in `Muxity.Api` (post-upload)
- [ ] Consumer in `Muxity.Transcoder` using `IHostedService` + manual ack
- [ ] Dead-letter queue for failed jobs (retry up to 3 times with backoff)

### 3.2 Transcoder Worker (`Muxity.Transcoder`)
- [ ] Worker registers itself in MongoDB on startup (NodeId, Hostname, HardwareCapabilities)
- [ ] Job claim: atomic MongoDB findAndModify to claim a job (prevents double-processing)
- [ ] Concurrency: configurable max parallel jobs per node (default 2)
- [ ] Progress reporting: update `TranscodeJob.Progress` (0–100) in MongoDB every ~5 seconds

### 3.3 FFmpeg Pipeline
- [ ] Hardware acceleration detection: probe for VAAPI/QSV (Intel) or NVENC (NVIDIA CUDA) on startup; fall back to software
- [ ] Transcode to adaptive bitrate HLS with the following default quality ladder:

  | Preset   | Resolution | Video Bitrate | Audio |
  |----------|-----------|---------------|-------|
  | 1080p    | 1920×1080 | 4500k         | 192k  |
  | 720p     | 1280×720  | 2500k         | 128k  |
  | 480p     | 854×480   | 1000k         | 128k  |
  | 360p     | 640×360   | 500k          | 96k   |

- [ ] Output: `.m3u8` master playlist + per-quality playlists + `.ts` segments (10s default)
- [ ] Segment output path: `hls/{videoId}/` in storage
- [ ] Master playlist: `hls/{videoId}/master.m3u8`
- [ ] On completion: update `Video.Status = Ready`, set `Video.DurationSeconds`, update `TranscodeJob` as complete

### 3.4 Thumbnail Generation
- [ ] After transcoding: extract frame at 10% of duration via FFmpeg (`-ss`, `-vframes 1`)
- [ ] Save to `thumbs/{videoId}/thumb.jpg` in storage
- [ ] Update `Video.ThumbnailPath`

### 3.5 Hardware Acceleration Config
- [ ] `Transcoder:HardwareAccel` setting: `Auto | QSV | NVENC | Software`
- [ ] Docker: expose `/dev/dri` for Intel QSV; `--gpus` for NVIDIA CUDA
- [ ] K8s: resource limits with `nvidia.com/gpu` or Intel GPU device plugin

**Deliverable:** Full transcode pipeline. Uploaded videos become streamable HLS within minutes.

---

## Phase 4 — Streaming API

**Goal:** Clients can stream videos via HLS with access control via streaming keys, offloaded to CDN.

### 4.1 Streaming Key Management (`Muxity.Api`)
- [ ] Streaming key auto-created when `Video.Status` transitions to `Ready`
- [ ] `GET /videos/{id}/streaming-key` — returns key for owner (create if missing)
- [ ] Keys are permanent by default; optional TTL for signed/expiring URLs

### 4.2 HLS Delivery (`Muxity.Streaming`)
- [ ] `GET /stream/{key}/master.m3u8` — validate key, resolve to videoId, redirect (302) to CDN master playlist URL
- [ ] `GET /stream/{key}/{quality}/{segment}` — validate key, proxy or redirect to CDN segment
- [ ] CDN URL signing: support CloudFront signed URLs or Cloudflare signed URLs (configurable)
- [ ] If no CDN configured: serve HLS directly from storage (dev mode)
- [ ] Rate limiting per streaming key (configurable requests/sec)

### 4.3 CDN Integration
- [ ] `ICdnProvider` interface: `GetSignedPlaylistUrl(videoId, quality)`, `GetSignedSegmentUrl(...)`
- [ ] `CloudFrontCdnProvider` implementation
- [ ] `CloudflareCdnProvider` implementation
- [ ] `PassthroughCdnProvider` (dev/local — serves directly)
- [ ] Registration via config: `Cdn:Provider = CloudFront | Cloudflare | Passthrough`

**Deliverable:** Videos stream via HLS. CDN handles segment delivery at scale.

---

## Phase 5 — Blazor Frontend

**Goal:** Full UI for video browsing, upload, management, and playback.

### 5.1 Project Setup (`Muxity.Web`)
- [ ] Blazor WebAssembly (standalone, talks to Api + Streaming via HTTP)
- [ ] Auth: OIDC login via Google/Microsoft (redirect flow), store JWT in memory + secure cookie
- [ ] HttpClient factory with auth header injection
- [ ] Tailwind CSS or MudBlazor for UI components

### 5.2 Pages & Components
- [ ] `/` — Public video feed (public videos, paginated grid with thumbnails)
- [ ] `/login` — Sign in with Google / Microsoft buttons
- [ ] `/dashboard` — Authenticated user's videos (list with status badges, visibility toggle)
- [ ] `/upload` — Drag-and-drop upload form (title, description, visibility, progress bar)
- [ ] `/videos/{id}` — Video detail page with player + metadata
- [ ] `/videos/{id}/edit` — Edit title/description/visibility

### 5.3 Video Player
- [ ] Embed Video.js with `videojs-http-streaming` (HLS.js backend)
- [ ] Load `master.m3u8` from Streaming API via streaming key URL
- [ ] Quality selector control (manual resolution switching)
- [ ] Poster image from thumbnail URL
- [ ] Loading/error states

### 5.4 Upload Experience
- [ ] Chunked upload (client-side chunk + reassemble on server, or presigned S3 multipart)
- [ ] Upload progress bar (XHR progress events or streaming response)
- [ ] Poll `/videos/{id}/upload-status` after upload completes to show transcode progress
- [ ] Redirect to video page when status = Ready

**Deliverable:** Full end-to-end user flow in the browser.

---

## Phase 6 — DevOps & Infrastructure

**Goal:** Production-ready deployments via Docker Compose and Kubernetes Helm chart.

### 6.1 Docker Compose
- [ ] Services: `api`, `streaming`, `transcoder`, `web`, `mongodb`, `rabbitmq`
- [ ] `transcoder` service variants: `transcoder-cpu`, `transcoder-intel`, `transcoder-nvidia` (profiles)
- [ ] Volumes: `storage-data` for local HLS/raw storage, `mongo-data`
- [ ] Healthchecks on all services
- [ ] `.env` file with all configurable secrets/settings
- [ ] `docker-compose.override.yml` for dev (hot reload, exposed ports)

### 6.2 Helm Chart (`infra/helm/muxity`)
- [ ] Charts for: `api`, `streaming`, `transcoder`, `web`
- [ ] Dependencies: MongoDB (Bitnami), RabbitMQ (Bitnami)
- [ ] Configmaps + Secrets for environment config
- [ ] Horizontal Pod Autoscaler for `streaming` and `api` (target CPU 70%)
- [ ] `transcoder` DaemonSet or Deployment with node affinity for GPU nodes
- [ ] PersistentVolumeClaim for local storage (or S3 config for cloud)
- [ ] Ingress with TLS (cert-manager / Let's Encrypt)
- [ ] Resource requests/limits per service

### 6.3 Observability
- [ ] Structured logging (Serilog → JSON) in all services
- [ ] Health endpoints: `GET /health` (liveness) + `GET /health/ready` (readiness) on all services
- [ ] OpenTelemetry traces (optional, Jaeger/Tempo compatible)
- [ ] Prometheus metrics endpoint (FastEndpoints middleware)

---

## Phase Summary

| Phase | Focus                    | Key Deliverable                                      |
|-------|--------------------------|------------------------------------------------------|
| 1     | Foundation               | Auth, models, DB — login works                       |
| 2     | Upload & Storage         | Upload API, storage abstraction, job enqueue         |
| 3     | Transcoding              | HLS output from distributed FFmpeg workers           |
| 4     | Streaming                | Streaming keys, CDN redirect, HLS delivery           |
| 5     | Frontend                 | Blazor UI with Video.js player                       |
| 6     | DevOps                   | Docker Compose + Helm chart, observability           |

---

## Key Technology Decisions

| Concern                  | Choice                              | Rationale                                               |
|--------------------------|-------------------------------------|---------------------------------------------------------|
| API framework            | FastEndpoints (.NET 10)             | Low ceremony, high perf endpoint model                  |
| Auth                     | OIDC (Google, Microsoft) + JWT      | No password management, federated identity              |
| Database                 | MongoDB                             | Flexible schema for video metadata, good .NET driver    |
| Message queue            | RabbitMQ                            | Durable job dispatch, dead-letter, worker ack semantics |
| Transcoding              | FFmpeg (distributed workers)        | Industry standard, hardware accel support               |
| HW acceleration          | Intel QSV (VAAPI) + NVIDIA NVENC    | Significant throughput vs software encode               |
| Storage                  | Local FS or S3-compatible           | Abstracted — works dev-local or cloud                   |
| CDN                      | CloudFront or Cloudflare (pluggable)| Offloads segment delivery for 50k+ concurrent clients   |
| Frontend                 | Blazor WASM                         | .NET ecosystem consistency                              |
| Player                   | Video.js + videojs-http-streaming   | HLS adaptive bitrate, broad browser support             |
| Container orchestration  | Docker Compose + Helm               | Dev simplicity + production K8s                         |
