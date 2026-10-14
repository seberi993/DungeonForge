---
name: deploy-web
description: Deploy DungeonForge by pushing to main. Use when the user says "push to main", "deploy", "ship it" or "roll out". Verifies the GitHub Action and the live Vercel site.
---

Prod frontend: https://dungeonforge-dist.vercel.app (Vercel project `dungeonforge-dist`, team `very-good-shooting`).
A push to `main` that touches `src/DungeonForge.Web/**` (or the workflow file) triggers `.github/workflows/deploy-web.yml`, which publishes the Blazor app and runs `vercel deploy --prod`. Do not deploy manually with the Vercel CLI.

## Steps
1. `git status`; make sure you are on `main`. Commit any pending intended changes (Conventional Commits, include the attribution lines from the session reminder). Never include the untracked `dist/` folder.
2. `dotnet build` and `dotnet test` must pass (warnings are errors). Stop and report on failure.
3. `git push origin main`.
4. If the push touched `src/DungeonForge.Web/**`, find the run (`gh` is NOT authenticated; use the public API):
   `curl -s "https://api.github.com/repos/seberi993/DungeonForge/actions/runs?per_page=1"`
   Poll the run id until `status` is `completed` (use a background command with an until-loop, not chained sleeps). `conclusion` must be `success`.
5. Confirm: `vercel ls dungeonforge-dist --scope very-good-shooting` shows a fresh Ready Production deployment, and `curl -s https://dungeonforge-dist.vercel.app/` returns 200.
6. Report: commit, run result, prod URL.

## Notes
- Pushes that only change `src/DungeonForge.Api/**` do not redeploy the frontend; the API runs on Render (`dungeonforge-api-ho2b.onrender.com`) and is deployed there. Say so in the report.
- If the Action fails at "Deploy to Vercel", the usual cause is a bad `VERCEL_TOKEN`/`VERCEL_ORG_ID`/`VERCEL_PROJECT_ID` secret ("Project not found"); the user must fix repo secrets. IDs: org `team_51ewVkr7V66JDRhLQgGctRf1`, project `prj_6sqoNiAdGLW2xi9Plk5NVc36GmCM`.
- If the site loads but calls fail with "Failed to fetch", check the Render API (`curl -i https://dungeonforge-api-ho2b.onrender.com/api/health`); a 502 with `x-render-routing: no-deploy` means Render has no live deploy.
