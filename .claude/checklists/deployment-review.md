# Checklist — Deployment Review (OEv2)

> Deployment is GitOps (CI → ECR → argocd). Confirm before any release; deploys/admin actions require
> explicit authorization.

- [ ] Image built for the changed service(s) (`oev2-<service>`), correct env account/region
      (dev 418541840646/ap-south-1, stg 172065883315/ap-south-1, prd 525375333516/ap-east-1).
- [ ] **Coordinated deploys** identified: MessagePack/SignalR contract change → OEv2 **and**
      platform-web together; `OE.Shared` contract change → bet-genius/platform/backoffice as needed.
- [ ] Sharding change (shard count/routing) → Adapter + all Sportsbook shards + argocd updated together.
- [ ] Config/secrets present in `argocd/apps/oddsengine/<service>/<env>.yaml` + External Secrets
      (`/<env>/apps/...`); no reliance on committed appsettings.
- [ ] New routing-key bindings present in argocd env (esp. anything beyond `r.oe.adapter.#.<NN>`).
- [ ] Per-instance services accounted for (Sportsbook 00–10; per-client Websocket/UOF).
- [ ] Telemetry env set (`Telemetry__Environment`, OTLP endpoint).
- [ ] Rollback path understood (image-updater tag pinning).
- [ ] **Production write/deploy authorized by a human.**
