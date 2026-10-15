# Cloud setup

How Kassensturz runs on Azure, step by step. Each phase says what was done and why, so the setup can be repeated and explained.

## Phase 0: Accounts and cost guardrails

### Concepts
| Azure word | Everyday version | Here |
|---|---|---|
| **Tenant** | Your login: who you are in the portal | Created automatically at sign-up |
| **Subscription** | The wallet that pays for what you create | Azure for Students |
| **Resource group** | A labelled box for one project's parts; deleting it deletes everything inside | `rg-kassensturz`; experiments get their own box |
| **Resource** | A thing that does work | The web app, the database |
| **Region** | The data centre it runs in | Sweden Central (EU) |

Every *Create* form in the portal asks for exactly these: subscription, resource group, region, name.

How Azure bills:
- Usage is metered per second or per request and charged monthly.
- **A pay-as-you-go subscription has no hard spending cap.** Budgets only send alerts.
- "€0" therefore comes from choosing plans that cannot bill (App Service Free, the SQL free offer with auto-pause) plus an alert as the last line of defence.

This project runs on **Azure for Students**:
- $100 credit for 12 months, and no credit card.
- When the credit runs out, the subscription is disabled rather than billed: a real hard cap on top of the free plans.
- **Limits:**
  - it must be renewed every year while a student;
  - otherwise convert it to pay-as-you-go (the free plans stay free), or the hosting stops. The phones keep their data, because the app is local-first.
  - An Azure Policy restricts the regions. Here: swedencentral, norwayeast, italynorth, belgiumcentral, francecentral (Portal → *Policy* → *Assignments* → "Allowed resource deployment regions").
  - Static Web Apps, the first choice, exists in none of those regions. Hence App Service.

### Steps
1. GitHub: create the repository and push the code.
2. Azure: sign up for Azure for Students with the university email.
3. Portal → *Resource groups* → *Create*:
   - name `rg-kassensturz`;
   - region **Sweden Central**.
4. Portal → *Cost Management* → *Budgets* → *Add* (it protects the credit and is a habit worth having):
   - scope: the subscription;
   - monthly amount €1;
   - an alert at 100 % of actual cost to your email address.

## Phase 1: Hosting on Azure App Service

### Concepts
- **Web server:** a program that answers the browser's requests. `Geld.Server` (ASP.NET Core) sends the app's files and, later, answers `/api`. The app itself still runs in the browser.
- **App Service plan vs Web App:**
  - the *plan* is the rented machine: its size and price;
  - the *Web App* is our program running on it.
  - Free (F1) shares a machine with others:
    - 60 CPU minutes a day;
    - 165 MB of outgoing traffic a day;
    - the app sleeps when idle and takes a few seconds to wake;
    - no custom domain.
  - For this app that is plenty: the phone loads the app from its offline cache, so the server rarely gets a request.
- **The address is permanent:**
  - the browser stores the app's data per address;
  - the Web App's name becomes `<name>.azurewebsites.net` and can't be changed;
  - so the name is chosen once, and the Web App is never deleted.
- **CI/CD:**
  - continuous integration builds and tests every push;
  - continuous delivery deploys every green build on `main`.
  - Here both run in GitHub Actions (`.github/workflows/ci.yml`).
- **Deploying without a stored password (OIDC):**
  - An *app registration* in Entra ID is an identity for a program, here "the GitHub workflow".
  - Its *federated credential* says: trust tokens that GitHub issues for this repository's `main` branch.
  - At deploy time, the workflow gets a short-lived token from GitHub and exchanges it for Azure access. Nothing secret is stored; the three ids in GitHub only say *who* to log in as.
  - The identity may only deploy to this one Web App (role *Website Contributor*, scope: the Web App). That's least privilege.
- **Compression and caching** (done by `Geld.Server`):
  - files are sent Brotli-compressed;
  - files whose name carries a hash are cached for a year;
  - everything else is re-checked on every load, so a new release is picked up.

### Steps
1. Portal → *Create a resource* → *Web App*:
   - resource group `rg-kassensturz`;
   - name: the permanent address (see above);
   - publish: **Code**;
   - runtime stack: **.NET 10** (if it isn't offered, tell me; then we publish with the runtime included);
   - operating system: **Linux**;
   - region: **Sweden Central**;
   - pricing plan: create a new one with **Free F1**;
   - leave the remaining tabs at their defaults and don't enable GitHub Actions deployment there; our own workflow deploys.
2. Portal → *Microsoft Entra ID* → *App registrations* → *New registration*:
   - name `kassensturz-deploy`;
   - "single tenant";
   - no redirect URI.
   - Note its *Application (client) ID* and *Directory (tenant) ID*.
3. The app registration → *Certificates & secrets* → *Federated credentials* → *Add credential*:
   - scenario "GitHub Actions deploying Azure resources";
   - organisation and repository: yours;
   - entity: **Branch**, `main`;
   - name `github-main`.
4. The Web App → *Access control (IAM)* → *Add role assignment*:
   - role **Website Contributor**;
   - members: the app registration `kassensturz-deploy`.
5. GitHub → repository → *Settings* → *Secrets and variables* → *Actions*:
   - secrets `AZURE_CLIENT_ID` (step 2), `AZURE_TENANT_ID` (step 2) and `AZURE_SUBSCRIPTION_ID` (Portal → *Subscriptions*);
   - under *Variables*, `AZURE_WEBAPP_NAME` with the Web App's name.
6. Push to `main`. The workflow tests, publishes, logs in to Azure and deploys.

### Checks
- `https://<name>.azurewebsites.net` opens the app; the first request after idle takes a few seconds.
- `/budget` still works after a reload.
- Installed to the home screen, the app works in flight mode.
- Compression: `curl -sI -H "Accept-Encoding: br" https://<name>.azurewebsites.net/styles.css` shows `Content-Encoding: br`.
