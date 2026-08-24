# Rehearsal Forecast

An ASP.NET Core MVC application on **.NET 10** that produces a full 36-month monthly financial forecast for a music rehearsal facility and computes the minimum constant per-square-foot rental price required for cumulative ending cash to reach and remain at zero or above from a user-selected target month through Month 36.

Inputs are captured through a server-rendered form. All calculations run inside a dependency-free core library (`RehearsalForecast.Core`) that uses only `decimal` arithmetic. Results can be viewed on the results page or exported as CSV.

---

## Table of contents

- [Quick start (macOS)](#quick-start-macos)
- [Business purpose](#business-purpose)
- [The target price: `Flat_Price_Per_Sqft` vs `Monthly_Price_Per_Sqft`](#the-target-price-flat_price_per_sqft-vs-monthly_price_per_sqft)
- [Financial formulas](#financial-formulas)
- [Cash-flow sign conventions](#cash-flow-sign-conventions)
- [Constant and Variable input modes](#constant-and-variable-input-modes)
- [Project organization](#project-organization)
- [Frontend styling](#frontend-styling)
- [Installing .NET 10](#installing-net-10)
- [Restore, build, run, debug, test — `dotnet` CLI](#restore-build-run-debug-test--dotnet-cli)
- [Working in VS Code](#working-in-vs-code)
- [Using the application](#using-the-application)
- [CSV export](#csv-export)
- [Workbook role](#workbook-role)
- [Container image](#container-image)
- [Terraform validation](#terraform-validation)
- [Deployment status and future direction](#deployment-status-and-future-direction)
- [Limitations of this phase](#limitations-of-this-phase)

---

## Quick start (macOS)

If you're on macOS (Intel or Apple Silicon), a bootstrap script installs everything you need — Homebrew, Git, the exact .NET 10 SDK pinned in `global.json`, Docker Desktop, and Terraform.

```bash
# 1. Clone and enter the project.
git clone <this-repo-url>
cd unitization-app

# 2. Install every dependency (idempotent — safe to re-run).
./scripts/install-mac-deps.sh

# 3. Open a new terminal, or reload the shell profile the script updated:
source ~/.zshrc
```

Once dependencies are in place, choose one of the three run paths below. All commands are run from the `unitization-app/` directory (the one containing `RehearsalForecast.sln`).

### Option A — Run locally with the .NET CLI

Best for iterative development and debugging.

```bash
dotnet restore RehearsalForecast.sln
dotnet run --project src/RehearsalForecast.Web
```

The listener URL is printed on startup (typically `http://localhost:5000`). Open it in a browser to reach the input form.

### Option B — Run in Docker

Best for reproducing the exact production runtime environment.

The easiest path is the wrapper script:

```bash
./scripts/run-docker.sh          # build if needed, run detached, wait for HTTP 200
./scripts/run-docker.sh status   # check container state
./scripts/run-docker.sh logs     # tail logs
./scripts/run-docker.sh down     # stop and remove
./scripts/run-docker.sh rebuild  # force a fresh --no-cache build
HOST_PORT=9090 ./scripts/run-docker.sh   # publish on a different host port
```

Or run Docker manually if you prefer:

```bash
docker build -t rehearsal-forecast:local .
docker run --rm -p 8080:8080 --name rehearsal-forecast rehearsal-forecast:local
```

Either way, open <http://localhost:8080/>. To stop the manual run press `Ctrl+C` (or `docker stop rehearsal-forecast` if you detached with `-d`).

The image is multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` for build, `mcr.microsoft.com/dotnet/aspnet:10.0` for runtime. It runs as the non-root `app` user, listens on `${PORT:-8080}`, and embeds no secrets.

### Option C — Run the test suite

```bash
dotnet test RehearsalForecast.sln -c Release
```

Runs the full xUnit + FsCheck.Xunit test suite (property tests execute at least 100 iterations each).

### Using the app

Once the app is running (Option A or B), the input page appears at the printed URL. Fill in the eight sections (Capital, Marketing, Operations, Building, Loan, Taxes, Owner Activity, Forecast Controls) and click **Calculate**. The results page shows `Flat_Price_Per_Sqft` plus the 36-month monthly forecast table, and offers an **Export CSV** button. See [Using the application](#using-the-application) for the field-by-field walkthrough.

### Troubleshooting

- **`dotnet` not found after running the script.** The script appends `DOTNET_ROOT` and PATH to `~/.zshrc`. Open a new terminal or run `source ~/.zshrc`.
- **`docker: command not found`.** Docker Desktop was installed but has never been launched. Open Docker Desktop from Applications once so it can register its CLI shims and start its background service.
- **`SDK not found` when running `dotnet` commands.** `global.json` pins the SDK to a specific 10.0 preview build. Re-run `./scripts/install-mac-deps.sh` — it detects the pinned version from `global.json` and installs it into `~/.dotnet` if missing.
- **Port 8080 already in use.** Change the host mapping: `docker run --rm -p 9090:8080 rehearsal-forecast:local` and browse to <http://localhost:9090/>.

---

## Business purpose

The owner is planning to purchase a warehouse and partition its interior into fixed 150-square-foot rehearsal units. Every unit is rented at the same per-square-foot price for the entire 36-month horizon. The owner wants a single answer:

> "What is the minimum flat rental price, applied uniformly across the 36-month window, such that the business is cash-positive from my chosen target month through Month 36?"

The `Rehearsal_Forecast_Application` answers that question. It accepts financial inputs (building geometry, capital spend, owner investment, loan terms, marketing spend, operations spend, tax rate, occupancy schedule, target cash-positive month, and beginning cash), builds the full 36-month forecast, and runs a bounded binary-search solver over the flat price until the resulting cash-flow forecast satisfies the cash-positive rule from the target month onward. The rounded-up-to-cents result is displayed and can be exported to CSV.

The core calculation engine has no dependency on ASP.NET Core, Excel, Terraform, or any UI abstraction. The MVC web app is a thin driver around it.

---

## The target price: `Flat_Price_Per_Sqft` vs `Monthly_Price_Per_Sqft`

`Flat_Price_Per_Sqft` is the **authoritative** output of the application. It is a single per-square-foot price that applies to the **entire 36-month period**. All 36 months are billed at the same per-square-foot rate; the solver's job is to find the minimum value that keeps the business cash-positive from the target month onward.

`Monthly_Price_Per_Sqft` is a **derived convenience value** shown on the results page. It is always defined as:

```
Monthly_Price_Per_Sqft = Flat_Price_Per_Sqft / 36
```

That is the "flat / 36" derivation. `Monthly_Price_Per_Sqft` is not an independently charged rate and is not stored as a user input. It exists only to make the flat-period price legible on a per-month basis.

Monthly revenue is computed from `Monthly_Price_Per_Sqft`:

```
Gross_Revenue[m] = Rented_Sqft[m] * Monthly_Price_Per_Sqft
```

which is arithmetically equivalent to `Rented_Sqft[m] * Flat_Price_Per_Sqft / 36`.

---

## Financial formulas

All monetary and rate values are `decimal`. The 36 months are indexed `m = 1..36`. `StandardUnitSize` is fixed at `150` square feet. `PayrollTaxRate` is fixed at `0.0765`.

### Building geometry (Requirement 3)

```
Rentable_Sqft      = Total_Sqft * Percentage_Available_For_Rent
Total_Rental_Units = ceil(Rentable_Sqft / 150)         // 0 when Rentable_Sqft = 0
```

### Occupancy — default schedule (Requirement 4)

When the user does not override occupancy, the default schedule is:

```
Occupancy_Rate[m] = min(m * 0.10, 1.00)                // saturates at 1.00 from m = 10
```

When the user overrides in Variable mode, `Occupancy_Rate[m]` is the user-supplied rate for that month (each in `[0, 1]`).

### Rented units and rented square feet

```
Rented_Units[m] = clamp(ceil(Total_Rental_Units * Occupancy_Rate[m]), 0, Total_Rental_Units)
Rented_Sqft[m]  = min(Rented_Units[m] * 150, Rentable_Sqft)
```

### Revenue (Requirement 5)

```
Monthly_Price_Per_Sqft = Flat_Price_Per_Sqft / 36
Gross_Revenue[m]       = Rented_Sqft[m] * Monthly_Price_Per_Sqft
Gross_Income[m]        = Gross_Revenue[m]              // COGS out of scope
```

### Marketing (Requirement 6)

```
Marketing_Total[m] = Print[m] + Search[m] + Social[m] + OtherMarketing[m]
```

### Operations and payroll tax (Requirement 7)

```
Payroll_Tax[m] = Wages[m] * 0.0765

Operations_Total[m] =
    Accounting[m] + Custodial[m] + Gas[m] + Insurance[m]
  + IT[m] + OfficeSupplies[m] + ProfessionalServices[m]
  + RentExpense[m] + Repairs[m] + Shipping[m]
  + PropertyTax[m] + Utilities[m] + Wages[m] + OtherOperations[m]
  + Payroll_Tax[m]
```

`Monthly_Loan_Interest` and `Monthly_Depreciation` are deliberately excluded from `Operations_Total`.

### Depreciation (Requirement 8)

```
Monthly_Depreciation = Total_Building_Cost / (Depreciation_Period_Years * 12)
```

Applied identically to every month. `Land_Value` and non-building capital line items are not depreciated.

### Capital, owner investment, and loan proceeds (Requirements 9, 10)

```
Total_Capital     = Equipment + TotalImprovementCost + BuildingPurchaseCost + OtherCapitalCost
Loan_Proceeds     = max(Total_Capital - Owner_Investment, 0)

Capital_Expenditures_In_Month[1]   = Total_Capital
Capital_Expenditures_In_Month[m>1] = 0

Owner_Investment_In_Month[1]       = Owner_Investment
Owner_Investment_In_Month[m>1]     = 0

Loan_Proceeds_In_Month[1]          = Loan_Proceeds
Loan_Proceeds_In_Month[m>1]        = 0
```

### Loan amortization (Requirement 11)

Standard declining-balance amortization over `Loan_Term_Months`. With monthly rate `i = Annual_Loan_Interest_Rate / 12`:

```
Monthly_Payment = Loan_Proceeds * (i * (1 + i)^Loan_Term_Months) / ((1 + i)^Loan_Term_Months - 1)

for m in 1..min(Loan_Term_Months, 36):
    Monthly_Loan_Interest[m]  = Balance[m] * i
    Monthly_Loan_Principal[m] = min(Monthly_Payment - Monthly_Loan_Interest[m], Balance[m])
    Loan_Ending_Balance[m]    = max(Balance[m] - Monthly_Loan_Principal[m], 0)

for m > Loan_Term_Months:
    Monthly_Loan_Interest[m]  = 0
    Monthly_Loan_Principal[m] = 0
    Loan_Ending_Balance[m]    = 0
```

Special cases:
- `Loan_Proceeds == 0` → every month emits zeros.
- `Annual_Loan_Interest_Rate == 0` → interest is zero and principal is straight-line `Loan_Proceeds / Loan_Term_Months`.
- Final-payment residual (rounding drift) is absorbed into the last principal payment so `Loan_Ending_Balance` reaches exactly `0` at term end.

All arithmetic is performed in `decimal`; the `(1 + i)^n` factor is computed by a decimal multiplication loop, not by a binary-float `Math.Pow` call.

### Income tax (Requirement 12)

Tax is applied only to positive pre-tax income months. Losses are not carried forward.

```
Expenses_Before_Income_Tax[m] =
    Marketing_Total[m] + Operations_Total[m]
  + Monthly_Loan_Interest[m] + Monthly_Depreciation

Pre_Tax_Income[m] = Gross_Income[m] - Expenses_Before_Income_Tax[m]
Income_Tax[m]     = max(Pre_Tax_Income[m], 0) * Income_Tax_Rate
Total_Expenses[m] = Expenses_Before_Income_Tax[m] + Income_Tax[m]
Net_Income[m]     = Gross_Income[m] - Total_Expenses[m]
```

### Cash flow (Requirement 13)

For `m = 1`, `Beginning_Cash[1] = BeginningCashMonth1` (a user input).
For `m in 2..36`, `Beginning_Cash[m] = Ending_Cash[m-1]`.

For every `m in 1..36`:

```
Ending_Cash[m] =
    Beginning_Cash[m]
  + Owner_Investment_In_Month[m]              // +
  + Loan_Proceeds_In_Month[m]                 // +
  + Net_Income[m]                             // + (already net of interest, depreciation, tax)
  + Monthly_Depreciation                      // + add-back of non-cash expense
  - Capital_Expenditures_In_Month[m]          // −
  - Monthly_Loan_Principal[m]                 // − principal only
  - Owner_Withdrawals                         // − constant every month
```

### Cash-positive rule (Requirement 14)

Given `target = Target_Cash_Positive_Month` in `[1, 36]`:

```
Cash_Positive_Rule_Satisfied =
    Ending_Cash[target] >= 0
    AND for every m in [target+1, 36]: Ending_Cash[m] >= 0

First_Sustained_Nonnegative_Month =
    smallest M in [1, 36] such that for every m in [M, 36], Ending_Cash[m] >= 0
    // "None" (null) when no such M exists

// When target = 36, the rule collapses to "Ending_Cash[36] >= 0" only.
```

### Solver (Requirement 15)

The solver runs a deterministic bounded binary search over `Flat_Price_Per_Sqft`, then rounds the winning candidate **up** to the nearest cent and re-verifies. If the rounded price fails the cash-positive rule post-rounding, it is raised by additional cents until it passes.

The output is the minimum `Flat_Price_Per_Sqft ≥ 0` satisfying the rule, rounded up to two decimal places. If no price within the safety limit satisfies the rule, the UI shows a validation-style solver-failure message and no price.

---

## Cash-flow sign conventions

The cash-flow line follows a strict additive convention: **items with a plus sign increase `Ending_Cash`; items with a minus sign decrease it.** Two subtleties are important:

1. **Depreciation add-back.** `Monthly_Depreciation` is already subtracted inside `Net_Income[m]` through `Expenses_Before_Income_Tax`. Because it is a non-cash expense, it is added back on the cash-flow line so it does not double-count.
2. **Principal-only debt service.** `Monthly_Loan_Interest[m]` was already treated as an expense inside `Net_Income[m]`. Only `Monthly_Loan_Principal[m]` reduces cash. Interest never appears a second time on the cash-flow line.

### Rounding modes

All intermediate arithmetic is performed at full `decimal` precision — no rounding. Rounding happens at three explicit boundaries only:

| Operation | Mode |
| --- | --- |
| `Total_Rental_Units`, `Rented_Units` | Mathematical ceiling |
| Solver's final `Flat_Price_Per_Sqft` rounding to cents | Round **up** (`ceil(x * 100) / 100`) |
| Display currency formatting on the results page | Banker's rounding (`MidpointRounding.ToEven`) |

The loan schedule's rounding drift is absorbed into the final month's principal so `Loan_Ending_Balance` at term end is exactly zero. Numeric arithmetic uses `decimal` throughout; `double` and `float` are prohibited in the calculation engine.

---

## Constant and Variable input modes

Some inputs are naturally scalar (for example `Total_Sqft`, `Owner_Investment`, `Annual_Loan_Interest_Rate`, `Loan_Term_Months`, `Depreciation_Period_Years`, `Income_Tax_Rate`, `BeginningCashMonth1`, `Target_Cash_Positive_Month`, `Owner_Withdrawals`). Others can vary month-to-month:

- All four marketing lines: `Print`, `Search`, `Social`, `OtherMarketing`.
- All 14 operations lines: `Accounting`, `Custodial`, `Gas`, `Insurance`, `IT`, `OfficeSupplies`, `ProfessionalServices`, `RentExpense`, `Repairs`, `Shipping`, `PropertyTax`, `Utilities`, `Wages`, `OtherOperations`.
- `Occupancy_Rate`.

For every schedulable input the input page renders a **Constant / Variable** radio group:

- **Constant** — one numeric field. That value applies to every month `m in 1..36`.
- **Variable** — 36 numeric fields (Month 1 through Month 36), each editable independently. In Variable mode all 36 values must be supplied.

The active mode is highlighted (filled radio, subtle background shade on the active subform, `.active` CSS class on the mode label). Switching modes is a deliberate action: clicking the other radio pre-populates the destination subform with the last-entered value (or, for `Occupancy_Rate` when switching to Variable, with the default schedule `min(m * 0.10, 1.00)`), so no data is silently lost.

The calculator never branches on `Mode`. Internally every schedulable input is a `MonthlySchedule<decimal>` (or `OccupancySchedule` for occupancy); the calculator always calls `schedule.At(m)`. This is why constant-mode with value `v` is guaranteed to produce the exact same forecast as variable-mode with 36 copies of `v`.

Validation is server-side. In Variable mode the server enforces exactly 36 entries; `Occupancy_Rate` entries must each be in `[0, 1]` and error messages identify the offending month.

---

## Project organization

The `unitization-app/` directory is a single .NET 10 solution.

```
unitization-app/
├── RehearsalForecast.sln
├── global.json                              # pins the SDK
├── README.md                                # this file
├── src/
│   ├── RehearsalForecast.Web/               # ASP.NET Core MVC web app
│   │   ├── Controllers/                     # ForecastController
│   │   ├── Views/                           # Index.cshtml, Results.cshtml, partials
│   │   ├── ViewModels/
│   │   ├── ModelBinders/                    # MonthlyScheduleModelBinder
│   │   ├── wwwroot/
│   │   │   └── css/
│   │   │       ├── site.css                 # app-specific styles (tabs, cards, table, hero)
│   │   │       └── vendor/
│   │   │           └── bootstrap.min.css    # Bootstrap 5.3.3 (MIT), vendored — no CDN, no JS bundle
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── RehearsalForecast.Core/              # Pure calculation library — no ASP.NET Core dependency
│       ├── Domain/                          # Input records & result types
│       ├── Schedules/                       # MonthlySchedule<T>, OccupancySchedule
│       ├── Forecast/                        # ForecastCalculator + calculators
│       ├── Loan/                            # LoanCalculator
│       ├── Solving/                         # PriceSolver
│       ├── Validation/                      # InputValidator
│       ├── Export/                          # CsvExporter
│       └── Constants/                       # ForecastConstants (StandardUnitSize, PayrollTaxRate, etc.)
├── tests/
│   └── RehearsalForecast.Core.Tests/        # xUnit + FsCheck.Xunit
├── infrastructure/
│   └── terraform/
│       ├── modules/cloud_run/               # Reusable Cloud Run module (validates only)
│       └── environments/dev/                # Dev environment root (validates only)
├── .github/
│   └── workflows/                           # ci.yml (CI validation only, no deployment)
└── .vscode/
    ├── launch.json
    └── tasks.json
```

`RehearsalForecast.Core` depends only on the .NET base class library. The web project references `RehearsalForecast.Core`; nothing depends on the web project. The test project references `RehearsalForecast.Core` and no other production project.

---

## Frontend styling

The web app uses **Bootstrap 5.3.3** as its base CSS framework, vendored locally at `src/RehearsalForecast.Web/wwwroot/css/vendor/bootstrap.min.css`. Application-specific styling lives in `wwwroot/css/site.css`, which loads after Bootstrap and layers on top of it.

Two design constraints shape how Bootstrap is integrated:

- **No CDN.** The `bootstrap.min.css` file is committed to the repository and served from the app's own `wwwroot/`. Nothing is fetched at runtime from an external host.
- **No client-side JavaScript.** The Bootstrap JS bundle is not loaded. Every interactive UI element on the site is either server-rendered or driven by pure CSS — most notably the eight-tab input page, which uses hidden radio buttons plus `:checked ~` sibling selectors to swap panels without any script.

What Bootstrap provides directly:

- Typography, focus rings, and light/system color defaults.
- Buttons (`.btn`, `.btn-primary`, `.btn-outline-secondary`).
- Native `<input>`/`<select>`/`<textarea>` styling — aliased in `site.css` via attribute selectors so the ~50 form fields don't need `class="form-control"` sprinkled on every element.
- Alerts (`.alert`, `.alert-warning`, `.alert-danger`).
- The `.container` and `.navbar` chrome used in `_Layout.cshtml`.

What `site.css` adds on top:

- The eight-tab input page: `.tab-radio`, `.tab-nav`, `.tab-nav__label`, `.tab-panel`, and the `#tab-N:checked ~ #panel-X` reveal rules. Panels with validation errors get badged with `.tab-nav__label--errored` and the earliest errored panel is auto-selected server-side.
- Section cards (`.section`) that wrap each input tab and each results block.
- The results hero (`.results-hero__*`) and summary grid (`.summary-grid`) used on the results page.
- The sticky-header, sticky-first-column 36-row forecast table (`.forecast-table`, `.table-scroll`).
- The Constant / Variable monthly schedule editor (`.schedule-mode-*`, `.schedule-monthly-grid__*`).
- Validation summary styling (`.validation-summary`, `.field-validation-error`, `.input-validation-error`).

Because `site.css` consumes Bootstrap's CSS custom properties (`--bs-primary`, `--bs-body-color`, `--bs-border-color`, `--bs-tertiary-bg`, etc.) rather than hard-coding colors, any Bootstrap theme swap (or setting `data-bs-theme="dark"` on `<html>`) propagates through the app-specific styles automatically.

---

## Installing .NET 10

The solution targets **.NET 10** (see `global.json`). Install the SDK from the official Microsoft download page.

> **macOS shortcut:** run `./scripts/install-mac-deps.sh` — see [Quick start (macOS)](#quick-start-macos). It reads the exact SDK version from `global.json` and installs it via the official Microsoft `dotnet-install.sh` script, alongside Docker Desktop and Terraform.

**macOS (Homebrew):**

```bash
brew install --cask dotnet-sdk
```

**Windows (winget):**

```powershell
winget install Microsoft.DotNet.SDK.10
```

**Linux:** follow the Microsoft install script or your distro's package instructions for the .NET 10 SDK.

Verify:

```bash
dotnet --list-sdks
# should include a 10.x entry
```

If a lower SDK is found first, `global.json` will still constrain builds to .NET 10; installing the 10.x SDK side-by-side is fine.

---

## Restore, build, run, debug, test — `dotnet` CLI

All commands are run from the `unitization-app/` directory (the one containing `RehearsalForecast.sln`).

**Restore:**

```bash
dotnet restore RehearsalForecast.sln
```

**Build:**

```bash
dotnet build RehearsalForecast.sln -c Release
```

Warnings are treated as errors; a clean build is required.

**Run the web app:**

```bash
dotnet run --project src/RehearsalForecast.Web
```

The listener URL is printed on startup (typically `http://localhost:5000`).

**Debug from the CLI:** launch the app with the debugger attach flag or attach after start-up. In practice VS Code (below) is the smoother workflow.

**Test:**

```bash
dotnet test RehearsalForecast.sln -c Release
```

Runs the full xUnit + FsCheck.Xunit test suite. Property tests execute at least 100 iterations each.

**Format check:**

```bash
dotnet format RehearsalForecast.sln --verify-no-changes
```

---

## Working in VS Code

Open the `unitization-app/` folder in VS Code with the C# extension installed. `.vscode/tasks.json` provides:

| Task label | Command |
| --- | --- |
| `build` (default build task) | `dotnet build RehearsalForecast.sln` |
| `restore` | `dotnet restore RehearsalForecast.sln` |
| `test` (default test task) | `dotnet test tests/RehearsalForecast.Core.Tests` |
| `watch` | `dotnet watch --project src/RehearsalForecast.Web run` |
| `publish` | `dotnet publish src/RehearsalForecast.Web -c Release -o publish` |

Run them from the command palette via **Tasks: Run Task**.

`.vscode/launch.json` provides:

- **Launch Web (RehearsalForecast.Web)** — builds and launches the web app under the debugger, with `ASPNETCORE_ENVIRONMENT=Development` and `ASPNETCORE_URLS=http://localhost:5000`. A `serverReadyAction` opens the URL in the browser once the app is listening.
- **Attach** — attach to an already-running instance (useful in combination with `dotnet watch`).

Press **F5** to launch, set breakpoints anywhere in `RehearsalForecast.Core` or `RehearsalForecast.Web`. Use the **Test Explorer** (or the CodeLens "Run Test" links) to execute individual tests.

---

## Using the application

1. Open the input page at the printed URL.
2. Fill in the eight labeled sections, in the order they appear on the page:
   1. **Capital** (`Equipment`, `TotalImprovementCost`, `BuildingPurchaseCost`, `OtherCapitalCost`),
   2. **Marketing** (four schedulable lines: `Print`, `Search`, `Social`, `OtherMarketing`),
   3. **Operations** (fourteen schedulable lines, including `Wages`),
   4. **Building** (`Total_Sqft`, `Percentage_Available_For_Rent`, `TotalBuildingCost`, `LandValue` (display-only), `Depreciation_Period_Years`, `Occupancy_Rate` schedule),
   5. **Loan** (`Annual_Loan_Interest_Rate`, `Loan_Term_Months`),
   6. **Taxes** (`Income_Tax_Rate`),
   7. **Owner Activity** (`Owner_Investment`, `Owner_Withdrawals`),
   8. **Forecast Controls** (`BeginningCashMonth1`, `Target_Cash_Positive_Month`).
3. For any schedulable input choose **Constant** (one value applies to all 36 months) or **Variable** (enter Month 1 through Month 36 individually).
4. Click **Calculate**. Validation runs server-side; on failure the input page redisplays with per-field and summary error messages, and the solver is not invoked.
5. On success the **Results** page shows:
   - `Flat_Price_Per_Sqft` (the authoritative 36-month per-sqft price) with a note that it applies to the entire 36-month period,
   - `Monthly_Price_Per_Sqft = Flat_Price_Per_Sqft / 36` beside it as a labeled convenience,
   - Summary metrics (`Total_Capital`, `Owner_Investment`, `Loan_Proceeds`, `Rentable_Sqft`, `Total_Rental_Units`),
   - `Cash_Positive_Rule_Satisfied` (Yes/No) and `First_Sustained_Nonnegative_Month` (a month index or "None"),
   - A horizontally scrollable 36-row table with all monthly columns.

The results page also exposes an **Export CSV** button.

---

## CSV export

Clicking **Export CSV** on the results page POSTs the current inputs back to the server, which re-runs validation → solve → export and returns a CSV file:

- Exactly **37 records** — one header row plus 36 data rows.
- Column order is fixed; the header row is stable across exports.
- Numeric fields use `CultureInfo.InvariantCulture` — period decimal separator, no thousands separator — so the file opens cleanly in any locale.
- Fields containing `,`, `"`, CR, or LF are RFC 4180 quoted.
- Line terminator is `\r\n`.
- Content type is `text/csv`; filename is `rehearsal-forecast-{yyyyMMdd-HHmmss}.csv`.
- No state is persisted server-side; the CSV is always recomputed from the resubmitted inputs.

---

## Workbook role

The workbook `Rehearsal Studio Forcast 2.xlsx` in this folder is **structural guidance only**. It informs which columns appear in the results table, what magnitudes typical values have, and how the categories are grouped. It is:

- **Not loaded at runtime.** The application has no dependency on Excel, Office automation, or any workbook parsing library.
- **Not loaded by the tests.** Structural parity tests use hand-transcribed expected values.
- **Not the source of truth.** Where the workbook and this application's specification disagree, the specification wins.

Deleting the workbook has no effect on build, test, run, or deployment.

---

## Container image

A multi-stage Dockerfile lives at the root of `unitization-app/` (alongside `.dockerignore`). Build the image from that directory:

```bash
docker build -t rehearsal-forecast:local .
```

Run it locally:

```bash
docker run --rm -p 8080:8080 rehearsal-forecast:local
# browse to http://localhost:8080/
```

The image is driven entirely by environment variables (for example `ASPNETCORE_URLS` and Cloud Run's injected `PORT`). It embeds no secrets, no credentials, and no environment-specific configuration.

---

## Terraform validation

The `infrastructure/terraform/` tree scaffolds a future Cloud Run deployment. **No resources are provisioned by this repository.** Only three local commands are exercised:

```bash
cd infrastructure/terraform/environments/dev
terraform fmt -check
terraform init -backend=false
terraform validate
```

All three must pass. `terraform apply` is intentionally not invoked — neither from scripts, nor from CI, nor from developer machines in this phase. See `infrastructure/terraform/environments/dev/README.md` for remote-state guidance (GCS backend with impersonation, out-of-band bucket creation) that a future deploy step would use.

---

## Deployment status and future direction

**Deployment is disabled in this phase.** The GitHub Actions workflow at `.github/workflows/ci.yml` performs CI validation only:

- `dotnet restore`, `dotnet build`, `dotnet test`, `dotnet publish`.
- `docker build` (image is built but never pushed).
- `terraform fmt -check`, `terraform init -backend=false`, `terraform validate`.

The CI workflow does not run `terraform apply`, does not run `docker push`, and does not authenticate to any cloud provider. Neither job needs cloud credentials.

**Why disabled:** this phase deliberately avoids cloud provisioning, secret management, and long-lived credentials. Adding deployment before the core application is stable would couple release mechanics to unrelated changes.

**Future path.** When deployment is enabled, a separate `.github/workflows/deploy.yml` will be added — `ci.yml` will not be edited. Deployment authentication will use **GitHub Actions workload identity federation** (`google-github-actions/auth@v2` with a `workload_identity_provider`) rather than long-lived JSON service-account keys. `.github/workflows/README.md` documents this intended future structure.

---

## Limitations of this phase

Everything below is intentionally **out of scope** for the current release. They are explicit non-goals, not oversights:

- **No database.** State is not persisted between requests. CSV export recomputes from the resubmitted inputs.
- **No authentication or authorization.** There is no login, no user identity, no session.
- **No cloud provisioning.** Terraform validates only. No `terraform apply`, no Cloud Run service is created, no artifact registry is populated.
- **No capital scheduling.** All capital expenditures land in Month 1; there is no per-month capital schedule.
- **No COGS.** `Gross_Income[m]` equals `Gross_Revenue[m]`; there is no cost-of-goods-sold line.
- **No variable `Owner_Withdrawals`.** `Owner_Withdrawals` is scalar-only: the same value is subtracted every month for 36 months.
- **`Standard_Unit_Size` fixed at 150.** Not user-editable. The constant lives in `ForecastConstants` and appears nowhere else in the code.
- **No runtime Excel dependency.** The workbook is structural reference only. The application never invokes Excel or Office automation.

These limitations are the shape of "phase 1 complete." Later phases can revisit any of them without changing the calculation engine's core invariants.
