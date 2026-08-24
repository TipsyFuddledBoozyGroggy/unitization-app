"""Manual-walkthrough driver for task 77.

Drives the running Kestrel process at http://localhost:5000 through:
  1. GET /                    → capture antiforgery cookie + form token.
  2. POST /Forecast/Calculate → sample-forecast valid submission.
  3. Verify results page contents.
  4. POST /Forecast/ExportCsv → download CSV, verify shape.
  5. POST /Forecast/Calculate → invalid submission (Pct_Available=1.5),
                                 verify server-side validation surfaces errors.

No third-party dependencies; uses stdlib urllib + http.cookiejar.
"""
from __future__ import annotations

import csv
import io
import json
import re
import sys
import urllib.parse
import urllib.request
from http.cookiejar import CookieJar
from pathlib import Path
from typing import Any

BASE = "http://localhost:5000"
OUT_DIR = Path(__file__).resolve().parent

# --- Sample-forecast domain values (from task 77) ---------------------------
SAMPLE_INPUTS: dict[str, str] = {
    # Capital
    "Capital.Equipment":            "10000",
    "Capital.TotalImprovementCost": "25000",
    "Capital.BuildingPurchaseCost": "200000",
    "Capital.OtherCapitalCost":     "5000",
    # Building
    "Building.TotalSqft":                    "10000",
    "Building.PercentageAvailableForRent":   "0.8",
    "Building.TotalBuildingCost":            "180000",
    "Building.LandValue":                    "50000",
    "Building.DepreciationPeriodYears":      "30",
    "Building.Occupancy.UseDefault":         "true",
    # Loan
    "Loan.AnnualLoanInterestRate": "0.08",
    "Loan.LoanTermMonths":         "60",
    # Taxes
    "Taxes.IncomeTaxRate": "0.25",
    # Owner activity
    "OwnerActivity.OwnerInvestment":  "100000",
    "OwnerActivity.OwnerWithdrawals": "5000",
    # Forecast controls
    "ForecastControls.BeginningCashMonth1":   "20000",
    "ForecastControls.TargetCashPositiveMonth": "24",
}

# Marketing & operations schedulables: Constant mode, sensible values.
MARKETING_CONSTANTS: dict[str, str] = {
    "Marketing.Print":          "200",
    "Marketing.Search":         "500",
    "Marketing.Social":         "300",
    "Marketing.OtherMarketing": "0",
}

OPERATIONS_CONSTANTS: dict[str, str] = {
    "Operations.Accounting":           "500",
    "Operations.Custodial":            "400",
    "Operations.Gas":                  "200",
    "Operations.Insurance":            "600",
    "Operations.It":                   "150",
    "Operations.OfficeSupplies":       "100",
    "Operations.ProfessionalServices": "300",
    "Operations.RentExpense":          "0",
    "Operations.Repairs":              "200",
    "Operations.Shipping":             "50",
    "Operations.PropertyTax":          "500",
    "Operations.Utilities":            "800",
    "Operations.Wages":                "6000",
    "Operations.OtherOperations":      "100",
}

FORECAST_MONTHS = 36


def build_form_fields(pct_available: str | None = None) -> list[tuple[str, str]]:
    """Assemble the full set of form fields for a Calculate/ExportCsv POST."""
    fields: list[tuple[str, str]] = []

    # Scalar inputs (may override PercentageAvailableForRent for the invalid case).
    for key, value in SAMPLE_INPUTS.items():
        if key == "Building.PercentageAvailableForRent" and pct_available is not None:
            fields.append((key, pct_available))
        else:
            fields.append((key, value))

    # Marketing schedules: Constant mode.
    for prefix, constant in MARKETING_CONSTANTS.items():
        fields.append((f"{prefix}.Mode", "Constant"))
        fields.append((f"{prefix}.ConstantValue", constant))
        for i in range(FORECAST_MONTHS):
            fields.append((f"{prefix}.MonthlyValues[{i}]", "0"))

    # Operations schedules: Constant mode.
    for prefix, constant in OPERATIONS_CONSTANTS.items():
        fields.append((f"{prefix}.Mode", "Constant"))
        fields.append((f"{prefix}.ConstantValue", constant))
        for i in range(FORECAST_MONTHS):
            fields.append((f"{prefix}.MonthlyValues[{i}]", "0"))

    # Occupancy schedule: default, pre-populated ramp per _OccupancyEditor.cshtml.
    for i in range(FORECAST_MONTHS):
        ramp = min((i + 1) * 0.10, 1.00)
        fields.append((f"Building.Occupancy.UserRates[{i}]", f"{ramp:.2f}"))

    return fields


def urlencode_fields(fields: list[tuple[str, str]]) -> bytes:
    return urllib.parse.urlencode(fields).encode("utf-8")


def extract_antiforgery_token(html: str) -> str:
    match = re.search(
        r'name="__RequestVerificationToken"[^>]*value="([^"]+)"',
        html,
    )
    if not match:
        raise RuntimeError("antiforgery token not found in HTML")
    return match.group(1)


def make_opener() -> tuple[urllib.request.OpenerDirector, CookieJar]:
    jar = CookieJar()
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    # Do NOT follow redirects — the ExportCsv failure path uses a redirect
    # that we want to observe explicitly.
    opener.add_handler(NoRedirect())
    return opener, jar


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):  # type: ignore[override]
        return None


def do_get(opener: urllib.request.OpenerDirector, path: str) -> urllib.request.http.client.HTTPResponse:  # noqa: SLF001
    req = urllib.request.Request(BASE + path, method="GET")
    return opener.open(req)


def do_post(
    opener: urllib.request.OpenerDirector,
    path: str,
    fields: list[tuple[str, str]],
) -> urllib.request.http.client.HTTPResponse:  # noqa: SLF001
    body = urlencode_fields(fields)
    req = urllib.request.Request(
        BASE + path,
        method="POST",
        data=body,
        headers={
            "Content-Type": "application/x-www-form-urlencoded",
            "Content-Length": str(len(body)),
        },
    )
    try:
        return opener.open(req)
    except urllib.error.HTTPError as exc:
        return exc  # type: ignore[return-value]


def read_body(resp) -> str:
    charset = resp.headers.get_content_charset() or "utf-8"
    return resp.read().decode(charset, errors="replace")


def read_bytes(resp) -> bytes:
    return resp.read()


def main() -> int:
    findings: dict[str, Any] = {}

    opener, _jar = make_opener()

    # --- 1. GET / -----------------------------------------------------------
    resp = do_get(opener, "/")
    assert resp.status == 200, f"GET / returned {resp.status}"
    index_html = read_body(resp)
    (OUT_DIR / "index.html").write_text(index_html, encoding="utf-8")
    findings["get_index"] = {
        "status": resp.status,
        "content_type": resp.headers.get("Content-Type"),
        "size": len(index_html),
        "has_h1": "Rehearsal Forecast — Inputs" in index_html,
    }
    token = extract_antiforgery_token(index_html)

    # --- 2. POST valid submission ------------------------------------------
    fields = build_form_fields()
    fields.append(("__RequestVerificationToken", token))
    resp = do_post(opener, "/Forecast/Calculate", fields)
    assert resp.status == 200, f"POST /Forecast/Calculate returned {resp.status}"
    results_html = read_body(resp)
    (OUT_DIR / "results.html").write_text(results_html, encoding="utf-8")

    # Verify results-page contents.
    checks = {
        "has_h1_results": "Rehearsal Forecast — Results" in results_html,
        "has_flat_price_label": "36-month flat price per sqft" in results_html,
        "has_monthly_equivalent_label": "Monthly equivalent = 36-month flat price / 36" in results_html,
        "has_cash_positive_rule_section": ">Cash-positive rule<" in results_html,
        "has_cash_positive_rule_row": "Cash-Positive Rule Satisfied" in results_html,
        "has_first_sustained_row": "First Sustained Nonnegative Month" in results_html,
        "has_summary_total_capital": ">Total Capital<" in results_html,
        "has_summary_loan_proceeds": ">Loan Proceeds<" in results_html,
        "has_summary_rentable_sqft": ">Rentable Sqft<" in results_html,
        "has_export_button": ">Export CSV<" in results_html,
        "has_table_scroll_wrapper": 'class="table-scroll"' in results_html,
        "has_forecast_table": 'class="forecast-table"' in results_html,
    }
    # Detail table row count: count <tbody> <tr> occurrences (must be 36).
    tbody_match = re.search(r"<tbody>(.*?)</tbody>", results_html, re.DOTALL)
    if tbody_match:
        checks["forecast_table_row_count"] = tbody_match.group(1).count("<tr>")
    # Column count from thead.
    thead_match = re.search(r"<thead>(.*?)</thead>", results_html, re.DOTALL)
    if thead_match:
        checks["forecast_table_col_count"] = thead_match.group(1).count("<th ")
    # Extract flat-price and monthly-equivalent numeric text.
    hero_matches = re.findall(
        r'<div class="results-hero__value">\s*(.*?)\s*</div>',
        results_html,
        re.DOTALL,
    )
    if hero_matches:
        checks["hero_values"] = [h.strip() for h in hero_matches]
    # Extract Cash_Positive_Rule status + first sustained month.
    def _dd_after(label: str) -> str | None:
        m = re.search(
            rf">{re.escape(label)}</dt>\s*<dd>\s*(.*?)\s*</dd>",
            results_html,
            re.DOTALL,
        )
        return m.group(1).strip() if m else None
    checks["cash_positive_rule_satisfied_value"] = _dd_after("Cash-Positive Rule Satisfied")
    checks["first_sustained_month_value"] = _dd_after("First Sustained Nonnegative Month")
    checks["target_cash_positive_month_value"] = _dd_after("Target Cash-Positive Month")
    findings["post_calculate_valid"] = {
        "status": resp.status,
        "checks": checks,
    }

    # --- 3. Extract the ExportCsv antiforgery token + hidden fields ---------
    # Re-extract token (the results page emits its own antiforgery token).
    export_token = extract_antiforgery_token(results_html)

    # Rebuild the export fields to match the round-tripped hidden inputs.
    # For occupancy default mode, the results view emits the ramp values as
    # hidden inputs (which we already send). The exact hidden-field content
    # doesn't matter for our purposes because we know the ForecastController
    # rebinds a fresh ForecastInputViewModel from the same field names.
    export_fields = build_form_fields()
    export_fields.append(("__RequestVerificationToken", export_token))

    resp = do_post(opener, "/Forecast/ExportCsv", export_fields)
    assert resp.status == 200, f"POST /Forecast/ExportCsv returned {resp.status}"
    csv_bytes = read_bytes(resp)
    (OUT_DIR / "forecast.csv").write_bytes(csv_bytes)
    ct = resp.headers.get("Content-Type")
    cd = resp.headers.get("Content-Disposition")
    # Parse CSV under invariant-culture-parseable format. Python's csv module
    # is culture-neutral by definition; float() uses "." as decimal separator,
    # matching the exporter's invariant-culture output.
    csv_text = csv_bytes.decode("utf-8")
    reader = csv.reader(io.StringIO(csv_text))
    rows = list(reader)
    header = rows[0] if rows else []
    data_rows = rows[1:]
    # Try parsing every numeric cell in every data row.
    parseable_cells = 0
    unparseable = []
    for r_idx, row in enumerate(data_rows, start=1):
        for c_idx, cell in enumerate(row):
            colname = header[c_idx] if c_idx < len(header) else f"col{c_idx}"
            if colname == "Cash_Positive_Status":
                if cell not in ("Yes", "No"):
                    unparseable.append((r_idx, colname, cell))
                continue
            try:
                float(cell)
                parseable_cells += 1
            except ValueError:
                unparseable.append((r_idx, colname, cell))
    findings["post_export_csv_valid"] = {
        "status": resp.status,
        "content_type": ct,
        "content_disposition": cd,
        "csv_size_bytes": len(csv_bytes),
        "total_records_in_csv": len(rows),
        "header_columns": header,
        "header_column_count": len(header),
        "data_row_count": len(data_rows),
        "flat_price_last_col": header[-1] if header else None,
        "parseable_numeric_cells": parseable_cells,
        "unparseable_cells": unparseable[:10],
    }

    # --- 4. Invalid submission: Percentage_Available_For_Rent = 1.5 ---------
    # Re-open connection with fresh cookie jar to simulate a new session.
    opener2, _jar2 = make_opener()
    resp = do_get(opener2, "/")
    assert resp.status == 200
    index_html2 = read_body(resp)
    token2 = extract_antiforgery_token(index_html2)

    invalid_fields = build_form_fields(pct_available="1.5")
    invalid_fields.append(("__RequestVerificationToken", token2))
    resp = do_post(opener2, "/Forecast/Calculate", invalid_fields)
    invalid_html = read_body(resp)
    (OUT_DIR / "invalid_response.html").write_text(invalid_html, encoding="utf-8")

    # The controller re-renders Index on validation failure — assert we see
    # the input page, NOT the results page (i.e., the solver never ran).
    findings["post_calculate_invalid"] = {
        "status": resp.status,
        "rerenders_index_page": "Rehearsal Forecast — Inputs" in invalid_html,
        "renders_results_page": "Rehearsal Forecast — Results" in invalid_html,
        "has_validation_summary_role_alert": 'class="validation-summary"' in invalid_html,
        "surfaces_pct_range_message": (
            "Percentage Available For Rent must be between 0 and 1." in invalid_html
        ),
        "surfaces_inline_error_span": (
            'asp-validation-for="Building.PercentageAvailableForRent"' in invalid_html
            or 'data-valmsg-for="Building.PercentageAvailableForRent"' in invalid_html
            or 'field-validation-error' in invalid_html
        ),
    }

    (OUT_DIR / "findings.json").write_text(
        json.dumps(findings, indent=2, default=str), encoding="utf-8"
    )
    print(json.dumps(findings, indent=2, default=str))
    return 0


if __name__ == "__main__":
    sys.exit(main())
