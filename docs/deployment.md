# Single-host Docker deployment

Pirate Forge publishes a Docker Compose model from the Aspire AppHost. The API
is internal-only. The Web service binds to host loopback and must be published
through a TLS-terminating reverse proxy on the same host.

## Prerequisites

- Docker 28 or later
- .NET 10 SDK
- Aspire CLI 13.4 or later
- A DNS name and TLS certificate for the public Git URL
- A host directory owned by the container user for persistent data

The .NET Linux images use UID `1654` by default. Prepare storage before the
first deployment:

```bash
sudo install -d -o 1654 -g 1654 /srv/pirateforge
sudo install -d -o 1654 -g 1654 \
  /srv/pirateforge/database \
  /srv/pirateforge/repositories \
  /srv/pirateforge/downloads \
  /srv/pirateforge/data-protection-keys/web
```

## Deployment parameters

Supply deployment values through the process environment. Do not commit them
to configuration files.

| Environment variable | Purpose | Example |
| --- | --- | --- |
| `Parameters__storage_path` | Absolute host persistence directory | `/srv/pirateforge` |
| `Parameters__web_port` | Loopback port used by the reverse proxy | `8080` |
| `Parameters__git_port` | Loopback port for Git smart HTTP | `8081` |
| `Parameters__allowed_hosts` | Public DNS host accepted by ASP.NET Core | `git.example.edu` |
| `Parameters__bootstrap_admin_email` | Initial administrator email | `admin@example.edu` |
| `Parameters__bootstrap_admin_password` | Initial administrator password | Secret |
| `Parameters__bootstrap_admin_display_name` | Initial administrator name | `Pirate Forge Administrator` |

The bootstrap values are idempotent and create the administrator only when it
does not already exist. Keep the password in a secret manager and rotate the
account password after the first successful sign-in.

Preview generated artifacts without starting containers:

```bash
aspire publish --environment Production --non-interactive
```

Deploy from the AppHost model:

```bash
aspire deploy --environment Production --non-interactive
```

The API image installs Git and runs as the non-root .NET container user. Both
services use `unless-stopped`. The full storage directory is mounted only into
the API; the Web service receives only its Data Protection key directory.

## TLS reverse proxy

Route normal browser traffic for `https://git.example.edu` to
`http://127.0.0.1:8080`. Route paths matching `/{repositoryName}.git/*` to
`http://127.0.0.1:8081`. Preserve request bodies and streaming responses on the
Git route, and forward:

```text
Host
X-Forwarded-For
X-Forwarded-Host
X-Forwarded-Proto
```

The proxy must replace, not append to, client-supplied forwarded headers. The
containers accept forwarded headers from their private Docker network because
the bridge gateway address is assigned dynamically. Keep both published ports
bound to loopback and do not attach untrusted containers to the Aspire network.

Do not expose either loopback port directly to the network. The generated
deployment enables ASP.NET Core forwarded-header processing. Verify `/health`
through the Web route before allowing traffic, then verify a clone through the
Git route.

The Web edge allows 20 login submissions per client IP per minute. The
internal API also applies an aggregate 1,000-request-per-minute safety valve to
authentication endpoints; this higher ceiling avoids turning normal classroom
login bursts from the Web container into a shared-client lockout.

Set proxy request-body and timeout limits high enough for expected Git pushes.
Pirate Forge intentionally does not impose an application-level receive-pack
body limit.

## Backup and restore

The storage directory is the complete backup unit: SQLite metadata, bare Git
repositories, generated downloads, and Data Protection keys must remain
together.

For a consistent offline backup:

1. Stop the Compose services without deleting them or the bind-mounted data.
2. Archive `/srv/pirateforge`, preserving ownership and permissions.
3. Start the services and confirm `/health`.
4. Copy the archive to encrypted storage on another machine.

To restore:

1. Stop the services.
2. Preserve the failed storage directory separately.
3. Restore the entire archive to the configured storage path.
4. Restore ownership for the container UID.
5. Deploy the same or newer application version; EF Core applies forward
   migrations during API startup.
6. Verify `/health`, sign-in, repository browsing, and a test clone.

Never copy only `pirate-forge.db`: repository contents and Data Protection keys
are required for a complete recovery.

## Upgrades and rollback

Take a full backup before every upgrade. Run the solution tests and
`aspire publish` before deploying. Database migrations are forward-only, so an
application rollback after a migration requires restoring the matching
pre-upgrade storage backup.

Generated Compose files are previews, not the source of truth. Make durable
changes in `PirateForgeGitServer.AppHost\AppHost.cs` and redeploy with Aspire.
