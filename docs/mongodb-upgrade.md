# Upgrade local MongoDB to 8.2.9

Upgrade in this order: **7.0 → 8.0.32 → 8.2.9**.
Start at your current version and follow the remaining steps in order.

See the [MongoDB upgrade path](https://www.mongodb.com/docs/upcoming/release-notes/8.2-upgrade-standalone/#upgrade-version-path).

Stop the Workflow API, then run the commands from `UvA.Workflow.Api`:

```bash
cd UvA.Workflow.Api
```

The commands use the local `admin` / `admin` credentials. Keep the existing
storage, user, and environment settings in `docker-compose.yaml`; only change
`image` at each step.

## 1. Prepare MongoDB 7.0

While your existing MongoDB 7.0 container is running, set its required feature
compatibility version:

```bash
docker compose exec -T mongodb mongosh --quiet \
  --username admin --password admin --authenticationDatabase admin \
  --eval 'db.adminCommand({setFeatureCompatibilityVersion: "7.0", confirm: true})'
```

## 2. Upgrade to MongoDB 8.0.32

Set `image: mongo:8.0.32` in `docker-compose.yaml`, then recreate the container:

```bash
docker compose up -d --timeout 30 mongodb
```

Once MongoDB has started, enable version 8.0 features:

```bash
docker compose exec -T mongodb mongosh --quiet \
  --username admin --password admin --authenticationDatabase admin \
  --eval 'db.adminCommand({setFeatureCompatibilityVersion: "8.0", confirm: true})'
```

## 3. Upgrade to MongoDB 8.2.9

Set `image: mongo:8.2.9` in `docker-compose.yaml`, then recreate the container:

```bash
docker compose up -d --timeout 30 mongodb
```

Once MongoDB has started, enable version 8.2 features:

```bash
docker compose exec -T mongodb mongosh --quiet \
  --username admin --password admin --authenticationDatabase admin \
  --eval 'db.adminCommand({setFeatureCompatibilityVersion: "8.2", confirm: true})'
```

Wait for each feature compatibility command to return `ok: 1` before continuing
to the next version. After the final step, restart the Workflow API.
