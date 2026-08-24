# Requirements Document

## Introduction

The Rehearsal_Forecast_Application is the first phase of a financial forecasting web application for a music rehearsal facility. The business will purchase an existing warehouse and construct individual rehearsal rental units inside it. The application accepts financial inputs, produces a complete 36-month monthly forecast (revenue, expenses, and cash flow), and computes the minimum constant 36-month flat rental price per square foot required for the business's ending cash to reach at least $0 by a user-selected target month and to remain nonnegative through Month 36.

This phase produces a server-rendered ASP.NET Core MVC application with a separated core calculation library, a CSV export, infrastructure scaffolding (Terraform), CI scaffolding (GitHub Actions), and a production-oriented Dockerfile. No database, no authentication, and no cloud provisioning are introduced. All monetary calculations use the `decimal` numeric type.

### Design decisions confirmed during clarification

The following clarifications resolve specification ambiguities and are binding for this phase:

1. Depreciable amount is Total_Building_Cost (from the Building category), not the sum of all capital expenditures. Land_Value is captured for display and future phases but is not used in any calculation in this phase.
2. Standard_Unit_Size is a fixed constant of 150 square feet in this phase; users cannot edit it.
3. Every capital line item is a single one-time input; the entire Total_Capital amount is recorded as a capital expenditure in Month 1. Capital scheduling is not supported in this phase.
4. The default Occupancy_Rate schedule follows Min(month × 10%, 100%); users may override with 36 monthly occupancy-rate percentages.
5. When Rented_Units × Standard_Unit_Size exceeds Rentable_Sqft, Rented_Sqft is clamped to Rentable_Sqft; Rented_Units retains its ceiling-based value.
6. Cost of Goods Sold (COGS) is out of scope in this phase. Gross_Income equals Gross_Revenue.
7. Marketing line items are Print, Search, Social, and Other (four inputs). Miscellaneous is not a separate line.
8. Owner_Withdrawals is a constant single value in this phase; no variable-mode toggle.
9. When no month in [1, 36] begins a sustained-nonnegative run through Month 36, the First_Sustained_Nonnegative_Month is displayed as "None".
10. Prices are USD; both the 36-month flat price per square foot and the monthly equivalent are displayed rounded to two decimal places. The Solver rounds its final answer UP to two decimals and re-verifies the cash-positive rule against the rounded value.
11. Percentage_Available_For_Rent is bounded to the inclusive range [0%, 100%].
12. Zero USD per square foot is a valid Solver answer when it satisfies the cash-positive rule.

## Glossary

- **Rehearsal_Forecast_Application**: The end-to-end system, comprising the Web_UI, Forecast_Calculator, Loan_Calculator, Solver, CSV_Exporter, and Input_Validator.
- **Web_UI**: The ASP.NET Core MVC controllers and Razor views that render the input page and the results page.
- **Forecast_Calculator**: The core component that produces a 36-month forecast for a given input set and a given candidate 36-month flat price per square foot.
- **Loan_Calculator**: The core component that produces a 36-month amortization schedule for given Loan_Proceeds, Annual_Loan_Interest_Rate, and Loan_Term_Months.
- **Solver**: The core component that finds the minimum nonnegative 36-month flat price per square foot that satisfies the Cash_Positive_Rule.
- **CSV_Exporter**: The core component that emits a CSV document representing all 36 monthly forecast records.
- **Input_Validator**: The server-side component that validates user-submitted inputs before any calculation is performed.
- **Constant_Mode**: An input mode where a single value applies to all 36 months.
- **Variable_Mode**: An input mode where 36 distinct monthly values are supplied.
- **Standard_Unit_Size**: The floor area of one rehearsal rental unit; fixed at 150 square feet in this phase; represented by a single named constant with no other literal occurrences of 150 in calculation code.
- **Total_Sqft**: The total floor area of the warehouse in square feet; user input.
- **Percentage_Available_For_Rent**: The share of Total_Sqft that can be rented, expressed as a decimal in [0, 1].
- **Rentable_Sqft**: Total_Sqft × Percentage_Available_For_Rent.
- **Total_Rental_Units**: Ceiling(Rentable_Sqft / Standard_Unit_Size).
- **Occupancy_Rate**: The share of Total_Rental_Units expected to be rented in a given month, expressed as a decimal in [0, 1]. Default schedule is Min(month × 0.10, 1.00) for months 1 through 10, and 1.00 for months 11 through 36.
- **Rented_Units**: Ceiling(Total_Rental_Units × Occupancy_Rate) for a given month, clamped so that Rented_Units ≤ Total_Rental_Units.
- **Rented_Sqft**: Min(Rented_Units × Standard_Unit_Size, Rentable_Sqft) for a given month.
- **Total_Building_Cost**: The depreciable cost of the building; a user input in the Building category; used only for depreciation.
- **Land_Value**: A user input in the Building category; captured for display; not used in any calculation in this phase.
- **Depreciation_Period_Years**: The number of years over which the building is depreciated; user input; must be > 0.
- **Monthly_Depreciation**: Total_Building_Cost / (Depreciation_Period_Years × 12).
- **Total_Capital**: Equipment + Total_Improvement_Cost + Building_Purchase_Cost + Other_Capital_Cost.
- **Capital_Expenditures_Month_1**: Total_Capital, recorded in Month 1; zero in all other months.
- **Owner_Investment**: A single nonnegative user input received in Month 1 only.
- **Owner_Withdrawals**: A single nonnegative user input applied to every month equally.
- **Loan_Proceeds**: Max(Total_Capital − Owner_Investment, 0), received in Month 1 only.
- **Annual_Loan_Interest_Rate**: A user input; the annual nominal interest rate for the loan; must be ≥ 0.
- **Monthly_Loan_Interest_Rate**: Annual_Loan_Interest_Rate / 12.
- **Loan_Term_Months**: A user input; the number of months over which the loan amortizes; must be > 0.
- **Monthly_Loan_Payment**: The fixed monthly payment that fully amortizes Loan_Proceeds over Loan_Term_Months at Monthly_Loan_Interest_Rate; zero when Loan_Proceeds = 0.
- **Loan_Beginning_Balance[m]**: The loan balance at the start of month m; Loan_Beginning_Balance[1] = Loan_Proceeds; Loan_Beginning_Balance[m+1] = Loan_Ending_Balance[m].
- **Monthly_Loan_Interest[m]**: Loan_Beginning_Balance[m] × Monthly_Loan_Interest_Rate.
- **Monthly_Loan_Principal[m]**: Min(Monthly_Loan_Payment − Monthly_Loan_Interest[m], Loan_Beginning_Balance[m]).
- **Loan_Ending_Balance[m]**: Max(Loan_Beginning_Balance[m] − Monthly_Loan_Principal[m], 0).
- **Marketing_Total[m]**: Print[m] + Search[m] + Social[m] + Other_Marketing[m].
- **Wages[m]**: A user input operations line item (constant or variable).
- **Payroll_Tax_Rate**: The derived-tax rate for wages, fixed at 0.0765.
- **Payroll_Tax[m]**: Wages[m] × Payroll_Tax_Rate.
- **Operations_Total[m]**: Sum of all operations line items in month m, including Wages[m] and Payroll_Tax[m] but excluding Monthly_Loan_Interest[m] and Monthly_Depreciation.
- **Income_Tax_Rate**: A user input decimal in [0, 1].
- **COGS_Rate_In_Scope**: Reserved for a future phase; not used in this phase.
- **Gross_Revenue[m]**: Rented_Sqft[m] × Monthly_Price_Per_Sqft.
- **Gross_Income[m]**: Gross_Revenue[m] in this phase (COGS is out of scope).
- **Expenses_Before_Income_Tax[m]**: Marketing_Total[m] + Operations_Total[m] + Monthly_Loan_Interest[m] + Monthly_Depreciation.
- **Pre_Tax_Income[m]**: Gross_Income[m] − Expenses_Before_Income_Tax[m].
- **Income_Tax[m]**: Max(Pre_Tax_Income[m], 0) × Income_Tax_Rate.
- **Total_Expenses[m]**: Expenses_Before_Income_Tax[m] + Income_Tax[m].
- **Net_Income[m]**: Gross_Income[m] − Total_Expenses[m].
- **Beginning_Cash[m]**: User-supplied for m = 1; Ending_Cash[m−1] otherwise.
- **Ending_Cash[m]**: Beginning_Cash[m] + Owner_Investment_In_Month[m] + Loan_Proceeds_In_Month[m] + Net_Income[m] + Monthly_Depreciation − Capital_Expenditures_In_Month[m] − Monthly_Loan_Principal[m] − Owner_Withdrawals.
- **Owner_Investment_In_Month[m]**: Owner_Investment for m = 1; 0 otherwise.
- **Loan_Proceeds_In_Month[m]**: Loan_Proceeds for m = 1; 0 otherwise.
- **Capital_Expenditures_In_Month[m]**: Total_Capital for m = 1; 0 otherwise.
- **Flat_Price_Per_Sqft**: The single 36-month per-square-foot price the user is charged; the Solver's output.
- **Monthly_Price_Per_Sqft**: Flat_Price_Per_Sqft / 36.
- **Target_Cash_Positive_Month**: A user-selected integer in [1, 36].
- **Cash_Positive_Rule**: Ending_Cash[Target_Cash_Positive_Month] ≥ 0 AND, for every month m in [Target_Cash_Positive_Month + 1, 36], Ending_Cash[m] ≥ 0.
- **First_Sustained_Nonnegative_Month**: The smallest integer M in [1, 36] such that for every m in [M, 36], Ending_Cash[m] ≥ 0; "None" when no such M exists.
- **Solver_Tolerance**: A documented decimal tolerance value used by the Solver to decide when the search interval has converged, defined as a code-level constant.
- **Solver_Safety_Limit**: A documented, configurable maximum number of iterations the Solver may perform before returning a solver-failure result.
- **Currency_Precision**: Two decimal places (USD cents).

## Requirements

### Requirement 1: Constant and Variable Input Modes

**User Story:** As a business planner, I want every applicable financial input to accept either a single constant value or 36 individual monthly values, so that I can model steady-state and month-varying scenarios in the same forecast.

#### Acceptance Criteria

1. WHERE an input supports Constant_Mode and Variable_Mode, THE Web_UI SHALL display a mode selector for that input that shows the current mode.
2. WHERE an input is in Constant_Mode, THE Forecast_Calculator SHALL treat the single constant value as the value for every month from Month 1 through Month 36.
3. WHEN the user switches an input from Constant_Mode to Variable_Mode, THE Web_UI SHALL populate all 36 monthly values with the constant value that was active immediately before the switch and require a deliberate user action to perform the switch.
4. WHERE an input is in Variable_Mode, THE Web_UI SHALL require exactly 36 monthly values and THE Input_Validator SHALL reject the submission when fewer than 36 monthly values are provided.
5. THE Web_UI SHALL make the currently selected mode visually distinguishable for each applicable input.
6. WHERE an input is Owner_Withdrawals, THE Web_UI SHALL provide a single constant input only and SHALL NOT expose Variable_Mode for that input.
7. WHERE an input is Total_Building_Cost, Land_Value, Beginning_Cash for Month 1, Owner_Investment, Total_Sqft, Percentage_Available_For_Rent, Annual_Loan_Interest_Rate, Loan_Term_Months, Depreciation_Period_Years, Income_Tax_Rate, Target_Cash_Positive_Month, Equipment, Total_Improvement_Cost, Building_Purchase_Cost, or Other_Capital_Cost, THE Web_UI SHALL treat that input as a single scalar with no monthly schedule.
8. WHERE a value is derived (Payroll_Tax, Monthly_Loan_Interest, Monthly_Loan_Principal, Monthly_Depreciation, Rentable_Sqft, Total_Rental_Units, Rented_Units, Rented_Sqft, Monthly_Price_Per_Sqft, Loan_Proceeds), THE Web_UI SHALL render that value as read-only.

### Requirement 2: Server-Side Input Validation

**User Story:** As a business planner, I want the application to reject invalid inputs on the server with clear messages, so that I never receive silently corrected forecasts.

#### Acceptance Criteria

1. IF Total_Sqft is negative, THEN THE Input_Validator SHALL reject the submission with a field-level error.
2. IF Percentage_Available_For_Rent is outside the inclusive range [0, 1], THEN THE Input_Validator SHALL reject the submission with a field-level error.
3. IF Depreciation_Period_Years is less than or equal to zero, THEN THE Input_Validator SHALL reject the submission with a field-level error.
4. IF Loan_Term_Months is less than or equal to zero, THEN THE Input_Validator SHALL reject the submission with a field-level error.
5. IF Annual_Loan_Interest_Rate is negative, THEN THE Input_Validator SHALL reject the submission with a field-level error.
6. IF Income_Tax_Rate is outside the inclusive range [0, 1], THEN THE Input_Validator SHALL reject the submission with a field-level error.
7. IF any expense input, capital input, wage input, Owner_Investment, Owner_Withdrawals, Total_Building_Cost, or Land_Value is negative, THEN THE Input_Validator SHALL reject the submission with a field-level error.
8. IF Target_Cash_Positive_Month is not an integer in [1, 36], THEN THE Input_Validator SHALL reject the submission with a field-level error.
9. IF a Variable_Mode schedule is submitted with fewer than 36 monthly values or with any non-numeric monthly entry, THEN THE Input_Validator SHALL reject the submission with a field-level error identifying the missing or invalid entries.
10. IF a user-supplied Occupancy_Rate entry is outside the inclusive range [0, 1], THEN THE Input_Validator SHALL reject the submission with a field-level error identifying the offending month.
11. THE Input_Validator SHALL perform all validations server-side and SHALL NOT rely on client-side JavaScript to enforce any validation rule.
12. WHEN a submission fails validation, THE Web_UI SHALL re-render the input page with a validation summary at the top of the page and inline field-level messages next to each offending field.
13. WHEN a submission fails validation, THE Forecast_Calculator SHALL NOT be invoked and THE Solver SHALL NOT be invoked.

### Requirement 3: Building Geometry and Unit Count

**User Story:** As a business planner, I want the application to compute rentable square footage and the total number of 150-square-foot units, so that I know the maximum inventory the facility can offer.

#### Acceptance Criteria

1. THE Forecast_Calculator SHALL compute Rentable_Sqft as Total_Sqft × Percentage_Available_For_Rent.
2. THE Forecast_Calculator SHALL compute Total_Rental_Units as Ceiling(Rentable_Sqft / Standard_Unit_Size), rounded up to the nearest whole unit.
3. WHEN Rentable_Sqft is 0, THE Forecast_Calculator SHALL set Total_Rental_Units to 0.
4. THE Forecast_Calculator SHALL treat Standard_Unit_Size as a single named constant equal to 150 and SHALL NOT expose Standard_Unit_Size as a user input in this phase.

### Requirement 4: Default Occupancy Schedule and Override

**User Story:** As a business planner, I want a built-in 10%-per-month occupancy ramp that I can override with 36 custom monthly percentages, so that I can quickly try scenarios other than the default ramp.

#### Acceptance Criteria

1. WHERE the user has not enabled Variable_Mode for Occupancy_Rate, THE Forecast_Calculator SHALL set Occupancy_Rate[m] to Min(m × 0.10, 1.00) for every m in [1, 36], yielding 0.10, 0.20, 0.30, 0.40, 0.50, 0.60, 0.70, 0.80, 0.90, and 1.00 for months 1 through 10 respectively and 1.00 for every month in [11, 36].
2. WHERE the user has enabled Variable_Mode for Occupancy_Rate, THE Forecast_Calculator SHALL use the 36 user-supplied Occupancy_Rate values, each of which is a decimal in the inclusive range [0, 1].
3. THE Forecast_Calculator SHALL compute Rented_Units[m] as Ceiling(Total_Rental_Units × Occupancy_Rate[m]) for every m in [1, 36].
4. THE Forecast_Calculator SHALL clamp Rented_Units[m] so that Rented_Units[m] is in the inclusive integer range [0, Total_Rental_Units] for every m in [1, 36].
5. THE Forecast_Calculator SHALL compute Rented_Sqft[m] as Min(Rented_Units[m] × Standard_Unit_Size, Rentable_Sqft) for every m in [1, 36].
6. THE Web_UI SHALL display Rented_Units[m] and Rented_Sqft[m] for every m in [1, 36] on the results page, labeled by month number.
7. WHEN the user switches Occupancy_Rate from the default schedule to Variable_Mode, THE Web_UI SHALL prepopulate all 36 monthly Occupancy_Rate fields with the values produced by Acceptance Criterion 1 and SHALL require a deliberate user action to perform the switch.

### Requirement 5: Revenue Calculation

**User Story:** As a business planner, I want the application to compute monthly rental revenue from the candidate flat 36-month price and the monthly rented square footage, so that revenue reflects both pricing and occupancy.

#### Acceptance Criteria

1. THE Forecast_Calculator SHALL compute Monthly_Price_Per_Sqft as Flat_Price_Per_Sqft / 36.
2. THE Forecast_Calculator SHALL compute Gross_Revenue[m] as Rented_Sqft[m] × Monthly_Price_Per_Sqft.
3. THE Forecast_Calculator SHALL compute Gross_Income[m] as Gross_Revenue[m] in this phase.
4. THE Forecast_Calculator SHALL apply the same Flat_Price_Per_Sqft to every month from Month 1 through Month 36.

### Requirement 6: Marketing Expense Total

**User Story:** As a business planner, I want the application to sum the four marketing line items each month, so that marketing appears as a single line in the forecast while remaining editable at the line-item level.

#### Acceptance Criteria

1. THE Web_UI SHALL provide inputs for exactly four marketing line items: Print, Search, Social, and Other_Marketing.
2. WHERE any marketing line item is in Constant_Mode, THE Forecast_Calculator SHALL apply the constant value to every month.
3. THE Forecast_Calculator SHALL compute Marketing_Total[m] as Print[m] + Search[m] + Social[m] + Other_Marketing[m] for every m in [1, 36].

### Requirement 7: Operations Expense Total and Payroll Tax Derivation

**User Story:** As a business planner, I want each operational cost captured individually and payroll tax computed automatically as 7.65% of wages, so that operations totals reflect all line items without manual arithmetic.

#### Acceptance Criteria

1. THE Web_UI SHALL provide inputs for the following operations line items: Accounting, Custodial, Gas, Insurance, IT, Office_Supplies, Professional_Services, Rent_Expense, Repairs, Shipping, Property_Tax, Utilities, Wages, and Other_Operations.
2. THE Forecast_Calculator SHALL compute Payroll_Tax[m] as Wages[m] × 0.0765.
3. THE Forecast_Calculator SHALL treat Payroll_Tax as a derived value and SHALL NOT accept a user-supplied Payroll_Tax value.
4. THE Forecast_Calculator SHALL compute Operations_Total[m] as the sum of all operations line items plus Payroll_Tax[m] for every m in [1, 36].
5. THE Forecast_Calculator SHALL NOT include Monthly_Loan_Interest[m] or Monthly_Depreciation in Operations_Total[m].

### Requirement 8: Depreciation

**User Story:** As a business planner, I want the building depreciated on a straight-line basis over a user-selected number of years, so that depreciation appears as a constant monthly expense in the forecast.

#### Acceptance Criteria

1. THE Forecast_Calculator SHALL compute Monthly_Depreciation as Total_Building_Cost / (Depreciation_Period_Years × 12).
2. THE Forecast_Calculator SHALL apply the same Monthly_Depreciation value to every month from Month 1 through Month 36.
3. THE Forecast_Calculator SHALL NOT subtract Land_Value from the depreciable amount in this phase.
4. THE Forecast_Calculator SHALL NOT include Equipment, Total_Improvement_Cost, Building_Purchase_Cost, or Other_Capital_Cost in the depreciable amount in this phase.
5. THE Web_UI SHALL display Land_Value on the input and results pages but SHALL NOT reference Land_Value in any calculation.

### Requirement 9: Total Capital and Capital Expenditures Timing

**User Story:** As a business planner, I want all capital line items to appear as a single Month 1 outflow, so that the initial investment is captured in the first month of the forecast.

#### Acceptance Criteria

1. THE Forecast_Calculator SHALL compute Total_Capital as Equipment + Total_Improvement_Cost + Building_Purchase_Cost + Other_Capital_Cost.
2. THE Forecast_Calculator SHALL record Capital_Expenditures_In_Month[1] as Total_Capital.
3. THE Forecast_Calculator SHALL record Capital_Expenditures_In_Month[m] as 0 for every m in [2, 36].
4. THE Web_UI SHALL display each capital line item as a single scalar input and SHALL NOT expose a monthly schedule for capital line items.

### Requirement 10: Owner Investment and Loan Proceeds Sizing

**User Story:** As a business planner, I want the loan sized to cover only what my owner investment does not, so that the loan is not over- or under-sized.

#### Acceptance Criteria

1. THE Forecast_Calculator SHALL compute Loan_Proceeds as Max(Total_Capital − Owner_Investment, 0).
2. IF Owner_Investment exceeds Total_Capital, THEN THE Forecast_Calculator SHALL set Loan_Proceeds to 0 and SHALL retain Total_Capital as the capital-expenditure amount.
3. THE Forecast_Calculator SHALL record Owner_Investment_In_Month[1] as Owner_Investment and Owner_Investment_In_Month[m] as 0 for every m in [2, 36].
4. THE Forecast_Calculator SHALL record Loan_Proceeds_In_Month[1] as Loan_Proceeds and Loan_Proceeds_In_Month[m] as 0 for every m in [2, 36].
5. THE Input_Validator SHALL accept Owner_Investment values that are strictly greater than Total_Capital and SHALL NOT reject the submission solely because Owner_Investment exceeds Total_Capital.

### Requirement 11: Declining-Balance Loan Amortization

**User Story:** As a business planner, I want a standard amortizing-loan schedule so that interest declines as principal is paid down, and only principal is deducted from cash flow.

#### Acceptance Criteria

1. WHEN Loan_Proceeds is 0, THE Loan_Calculator SHALL set Monthly_Loan_Payment to 0 and SHALL set Monthly_Loan_Interest[m], Monthly_Loan_Principal[m], Loan_Beginning_Balance[m], and Loan_Ending_Balance[m] to 0 for every m in [1, 36].
2. WHEN Loan_Proceeds is positive AND Annual_Loan_Interest_Rate is 0, THE Loan_Calculator SHALL set Monthly_Loan_Payment to Loan_Proceeds / Loan_Term_Months, SHALL set Monthly_Loan_Interest[m] to 0 for every m in [1, 36], and SHALL set Monthly_Loan_Principal[m] to Min(Monthly_Loan_Payment, Loan_Beginning_Balance[m]).
3. WHEN Loan_Proceeds is positive AND Annual_Loan_Interest_Rate is positive, THE Loan_Calculator SHALL compute Monthly_Loan_Payment as the fixed payment that fully amortizes Loan_Proceeds over Loan_Term_Months at Monthly_Loan_Interest_Rate using the standard amortization formula.
4. THE Loan_Calculator SHALL set Loan_Beginning_Balance[1] to Loan_Proceeds.
5. THE Loan_Calculator SHALL compute Monthly_Loan_Interest[m] as Loan_Beginning_Balance[m] × Monthly_Loan_Interest_Rate for every m in [1, 36].
6. THE Loan_Calculator SHALL compute Monthly_Loan_Principal[m] as Min(Monthly_Loan_Payment − Monthly_Loan_Interest[m], Loan_Beginning_Balance[m]) for every m in [1, 36].
7. THE Loan_Calculator SHALL compute Loan_Ending_Balance[m] as Max(Loan_Beginning_Balance[m] − Monthly_Loan_Principal[m], 0) for every m in [1, 36].
8. THE Loan_Calculator SHALL set Loan_Beginning_Balance[m+1] to Loan_Ending_Balance[m] for every m in [1, 35].
9. WHILE the loan has a positive Loan_Beginning_Balance AND Annual_Loan_Interest_Rate is positive, THE Loan_Calculator SHALL produce a Monthly_Loan_Interest sequence in which Monthly_Loan_Interest[m+1] is less than or equal to Monthly_Loan_Interest[m].
10. WHERE Loan_Term_Months is less than 36, THE Loan_Calculator SHALL cause Loan_Beginning_Balance[m] to be 0 for every m greater than Loan_Term_Months and SHALL cause Monthly_Loan_Interest[m] and Monthly_Loan_Principal[m] to be 0 for every m greater than Loan_Term_Months.
11. WHERE Loan_Term_Months is greater than 36, THE Loan_Calculator SHALL produce a Loan_Ending_Balance[36] that is greater than 0 and SHALL NOT force early payoff within the 36-month forecast window.
12. IF the standard monthly payment applied to the final month would leave a nonzero rounding residual, THEN THE Loan_Calculator SHALL adjust the final Monthly_Loan_Principal so that Loan_Ending_Balance[Loan_Term_Months] is exactly 0.
13. THE Forecast_Calculator SHALL treat Monthly_Loan_Interest[m] as an expense within Expenses_Before_Income_Tax[m].
14. THE Forecast_Calculator SHALL subtract only Monthly_Loan_Principal[m] from Ending_Cash[m] as loan servicing and SHALL NOT subtract Monthly_Loan_Interest[m] a second time in the cash-flow forecast.

### Requirement 12: Income Tax on Positive Pre-Tax Income Only

**User Story:** As a business planner, I want income tax applied only to positive monthly pre-tax income with no loss carryforward, so that loss months incur zero tax and the model stays simple for this phase.

#### Acceptance Criteria

1. THE Forecast_Calculator SHALL compute Expenses_Before_Income_Tax[m] as Marketing_Total[m] + Operations_Total[m] + Monthly_Loan_Interest[m] + Monthly_Depreciation.
2. THE Forecast_Calculator SHALL compute Pre_Tax_Income[m] as Gross_Income[m] − Expenses_Before_Income_Tax[m].
3. THE Forecast_Calculator SHALL compute Income_Tax[m] as Max(Pre_Tax_Income[m], 0) × Income_Tax_Rate.
4. WHEN Pre_Tax_Income[m] is less than or equal to 0, THE Forecast_Calculator SHALL set Income_Tax[m] to 0.
5. THE Forecast_Calculator SHALL compute Total_Expenses[m] as Expenses_Before_Income_Tax[m] + Income_Tax[m].
6. THE Forecast_Calculator SHALL compute Net_Income[m] as Gross_Income[m] − Total_Expenses[m].
7. THE Forecast_Calculator SHALL NOT carry losses from one month forward to reduce Income_Tax in any subsequent month.

### Requirement 13: Monthly Cash-Flow Forecast

**User Story:** As a business planner, I want a 36-month cash-flow forecast that rolls forward from a user-supplied opening balance, so that I can see whether the business survives month by month.

#### Acceptance Criteria

1. THE Forecast_Calculator SHALL produce exactly 36 monthly cash-flow records, one per month from Month 1 through Month 36.
2. THE Forecast_Calculator SHALL set Beginning_Cash[1] to the user-supplied opening cash value.
3. THE Forecast_Calculator SHALL set Beginning_Cash[m] to Ending_Cash[m − 1] for every m in [2, 36].
4. THE Forecast_Calculator SHALL compute Ending_Cash[m] as Beginning_Cash[m] + Owner_Investment_In_Month[m] + Loan_Proceeds_In_Month[m] + Net_Income[m] + Monthly_Depreciation − Capital_Expenditures_In_Month[m] − Monthly_Loan_Principal[m] − Owner_Withdrawals.
5. THE Forecast_Calculator SHALL add Monthly_Depreciation back in the cash-flow forecast because Monthly_Depreciation is a non-cash expense already subtracted from Net_Income[m].
6. THE Forecast_Calculator SHALL apply the same Owner_Withdrawals value to every month from Month 1 through Month 36.
7. THE Forecast_Calculator SHALL document sign conventions such that additions increase Ending_Cash and subtractions decrease Ending_Cash, matching the formula in Acceptance Criterion 4.

### Requirement 14: Cash-Positive Rule

**User Story:** As a business planner, I want to know whether a candidate price is cash-positive from my selected target month through Month 36, so that I have a single pass/fail signal for the pricing outcome.

#### Acceptance Criteria

1. THE Forecast_Calculator SHALL evaluate the Cash_Positive_Rule as: Ending_Cash[Target_Cash_Positive_Month] ≥ 0 AND, for every m in [Target_Cash_Positive_Month + 1, 36], Ending_Cash[m] ≥ 0.
2. THE Forecast_Calculator SHALL treat months strictly earlier than Target_Cash_Positive_Month as unconstrained by the Cash_Positive_Rule.
3. THE Web_UI SHALL display whether the Cash_Positive_Rule is satisfied by the computed Flat_Price_Per_Sqft.
4. THE Forecast_Calculator SHALL compute First_Sustained_Nonnegative_Month as the smallest integer M in [1, 36] such that Ending_Cash[m] ≥ 0 for every m in [M, 36].
5. WHEN no month in [1, 36] begins a sustained-nonnegative run through Month 36, THE Forecast_Calculator SHALL emit First_Sustained_Nonnegative_Month with the value "None" and THE Web_UI and THE CSV_Exporter SHALL render that value as "None".

### Requirement 15: Target-Price Solver

**User Story:** As a business planner, I want the application to search for the minimum 36-month flat rental price per square foot that satisfies the Cash_Positive_Rule, so that I have a clear target price to charge.

#### Acceptance Criteria

1. THE Solver SHALL search for the minimum nonnegative Flat_Price_Per_Sqft satisfying the Cash_Positive_Rule for the given inputs.
2. THE Solver SHALL use a deterministic bounded binary search over Flat_Price_Per_Sqft.
3. THE Solver SHALL begin its search at Flat_Price_Per_Sqft = 0.
4. WHEN Flat_Price_Per_Sqft = 0 already satisfies the Cash_Positive_Rule, THE Solver SHALL return 0 as the minimum Flat_Price_Per_Sqft.
5. THE Solver SHALL expand its upper bound geometrically until either the Cash_Positive_Rule is satisfied at that upper bound or Solver_Safety_Limit is reached.
6. THE Solver SHALL use Solver_Tolerance as a documented decimal convergence tolerance and SHALL terminate binary search when the current interval width is less than or equal to Solver_Tolerance.
7. THE Solver SHALL invoke a fresh Forecast_Calculator run for each candidate Flat_Price_Per_Sqft and SHALL NOT reuse cached forecasts across candidates.
8. THE Solver SHALL round the final Flat_Price_Per_Sqft UP to Currency_Precision before returning it.
9. THE Solver SHALL re-run the Forecast_Calculator with the rounded Flat_Price_Per_Sqft and SHALL verify that the Cash_Positive_Rule is still satisfied at the rounded value.
10. IF the rounded Flat_Price_Per_Sqft does not satisfy the Cash_Positive_Rule, THEN THE Solver SHALL increment the rounded value by one unit of Currency_Precision and re-verify, repeating until the Cash_Positive_Rule is satisfied.
11. IF the Solver reaches Solver_Safety_Limit without finding a Flat_Price_Per_Sqft that satisfies the Cash_Positive_Rule, THEN THE Solver SHALL return a solver-failure result with a human-readable message and SHALL NOT throw an unhandled exception and SHALL NOT loop indefinitely.
12. THE Solver SHALL use the decimal numeric type for every intermediate value.
13. THE Solver SHALL be a distinct component from the Forecast_Calculator and SHALL NOT depend on ASP.NET Core, Razor, Terraform, or any UI abstraction.

### Requirement 16: Results Page Content

**User Story:** As a business planner, I want the results page to prominently show the computed target price and the supporting metrics, so that I can share the outcome without scrolling through the entire forecast.

#### Acceptance Criteria

1. THE Web_UI SHALL display Flat_Price_Per_Sqft prominently on the results page, labeled to indicate that it is a single price for the entire 36-month period.
2. THE Web_UI SHALL display Monthly_Price_Per_Sqft prominently on the results page, labeled as "Monthly equivalent = 36-month flat price / 36" so that users do not confuse it with a monthly or annual rate.
3. THE Web_UI SHALL display Target_Cash_Positive_Month, whether the Cash_Positive_Rule is satisfied, and First_Sustained_Nonnegative_Month on the results page.
4. THE Web_UI SHALL display Total_Capital, Owner_Investment, Loan_Proceeds, Rentable_Sqft, and Total_Rental_Units on the results page.
5. THE Web_UI SHALL display a complete 36-month forecast table containing at least the following columns for each month: Month, Occupancy_Rate, Total_Rental_Units, Rented_Units, Rented_Sqft, Monthly_Price_Per_Sqft, Gross_Revenue, Gross_Income, Marketing_Total, Operations_Total, Wages, Payroll_Tax, Loan_Beginning_Balance, Monthly_Loan_Payment, Monthly_Loan_Interest, Monthly_Loan_Principal, Loan_Ending_Balance, Monthly_Depreciation, Pre_Tax_Income, Income_Tax, Total_Expenses, Net_Income, Beginning_Cash, Owner_Investment_In_Month, Loan_Proceeds_In_Month, Capital_Expenditures_In_Month, Owner_Withdrawals, Ending_Cash, and a Cash_Positive_Status indicator.
6. THE Web_UI SHALL render Flat_Price_Per_Sqft and Monthly_Price_Per_Sqft rounded to two decimal places with a leading "$" symbol.
7. WHERE the results table is displayed on a viewport narrower than the table's natural width, THE Web_UI SHALL provide horizontal scrolling so that all columns remain readable.

### Requirement 17: Input Page Organization

**User Story:** As a business planner, I want the input page organized into clearly labeled sections, so that I can locate related inputs quickly.

#### Acceptance Criteria

1. THE Web_UI SHALL group inputs on the input page into the following labeled sections: Capital, Marketing, Operations, Building, Loan, Taxes, Owner_Activity, and Forecast_Controls.
2. THE Web_UI SHALL present a single, clearly marked Calculate action on the input page that submits the form to the server.
3. WHEN the Calculate action is submitted, THE Web_UI SHALL invoke server-side validation before invoking the Forecast_Calculator or the Solver.
4. WHEN validation succeeds, THE Web_UI SHALL render the results page with the values from the current submission.
5. WHEN validation fails, THE Web_UI SHALL re-render the input page preserving the user's inputs and displaying validation messages.

### Requirement 18: CSV Export

**User Story:** As a business planner, I want to export the 36-month forecast to a CSV file, so that I can share results with advisors and open them in a spreadsheet.

#### Acceptance Criteria

1. THE CSV_Exporter SHALL produce a CSV document containing exactly one header row followed by exactly 36 data rows, one per month.
2. THE CSV_Exporter SHALL emit a stable, invariant header row whose column names and order do not change between exports of forecasts with equivalent structure.
3. THE CSV_Exporter SHALL emit the same set of columns that the results page displays for each monthly forecast row plus the Flat_Price_Per_Sqft either as a repeated column value on each row or as an explicit clearly identified column.
4. THE CSV_Exporter SHALL escape any field value that contains a comma, a double quote, a carriage return, or a line feed by wrapping the field in double quotes and doubling any internal double quotes.
5. THE CSV_Exporter SHALL emit all numeric values using an invariant culture with a period as the decimal separator and no thousands separator.
6. IF any user-controlled text field begins with `=`, `+`, `-`, `@`, tab, or carriage return, THEN THE CSV_Exporter SHALL prefix that field with a single leading apostrophe so that no exported cell can be interpreted as a spreadsheet formula.
7. THE CSV_Exporter SHALL respond with the HTTP `Content-Type: text/csv` header and with a `Content-Disposition` header specifying a filename that identifies the export as a rehearsal-forecast CSV.
8. THE CSV_Exporter SHALL correspond exactly to the inputs submitted on the current request and SHALL NOT persist data to a database or to server-side temporary storage between requests.
9. WHEN the CSV_Exporter is invoked with a valid forecast, THE CSV_Exporter SHALL emit output whose serialization is deterministic for a given input set, meaning that repeated exports of the same input produce byte-for-byte identical CSV documents.

### Requirement 19: Decimal Arithmetic and Numeric Type Discipline

**User Story:** As a business planner, I want all financial arithmetic performed with decimal precision, so that rounding errors from binary floats never affect the price recommendation.

#### Acceptance Criteria

1. THE Forecast_Calculator, Loan_Calculator, and Solver SHALL use the `decimal` C# type for every monetary value, rate, ratio, and intermediate calculation.
2. THE Forecast_Calculator SHALL NOT convert monetary values to `double`, `float`, or any binary floating-point type in intermediate calculations.
3. WHERE a rounding operation is required, THE Forecast_Calculator SHALL use banker's-rounding-aware or documented explicit rounding modes and SHALL document the rounding mode chosen.

### Requirement 20: Architectural Separation of Core Calculation Engine

**User Story:** As a developer, I want the core calculation engine independent of ASP.NET Core and UI concerns, so that I can unit-test it without a web host and evolve the UI without touching business logic.

#### Acceptance Criteria

1. THE Forecast_Calculator, Loan_Calculator, and Solver SHALL reside in a project distinct from the ASP.NET Core web project.
2. THE Forecast_Calculator, Loan_Calculator, and Solver SHALL NOT reference ASP.NET Core, Razor, Terraform, Google Cloud SDKs, or any UI abstraction.
3. THE Forecast_Calculator, Loan_Calculator, and Solver SHALL depend only on the .NET base class library and on abstractions declared within the same core project.
4. WHERE dependency injection is used, THE core projects SHALL define interfaces that improve testability or separate meaningful responsibilities, and SHALL NOT introduce interfaces solely for indirection.

### Requirement 21: .NET 10 Solution and Tooling

**User Story:** As a developer, I want the solution to build, run, debug, and test from VS Code and the dotnet CLI with .NET 10, so that I can work productively without additional IDE setup.

#### Acceptance Criteria

1. THE Rehearsal_Forecast_Application SHALL target .NET 10 and SHALL restore and build using the `dotnet` CLI without additional package sources.
2. THE Rehearsal_Forecast_Application SHALL include a solution file and project files organized as `src/RehearsalForecast.Web/`, `src/RehearsalForecast.Core/`, and `tests/RehearsalForecast.Core.Tests/`.
3. THE Rehearsal_Forecast_Application SHALL include `.vscode/launch.json` and `.vscode/tasks.json` supporting build, run, debug, and test workflows from VS Code.
4. THE Rehearsal_Forecast_Application SHALL include a `.gitignore` appropriate for a .NET solution.
5. THE Rehearsal_Forecast_Application SHALL run from `dotnet run` in the `src/RehearsalForecast.Web/` project directory and SHALL be reachable at a documented local URL.
6. THE Rehearsal_Forecast_Application SHALL NOT introduce Razor Pages, Blazor, a JavaScript SPA framework, a database, authentication, or cloud-provider client libraries in this phase.

### Requirement 22: Unit-Test Coverage for Core Financial Logic

**User Story:** As a developer, I want a focused unit-test suite that exercises the core financial formulas, so that regressions in business rules are caught before release.

#### Acceptance Criteria

1. THE test project SHALL use xUnit as its test framework.
2. THE test project SHALL include tests covering: constant-to-36-month expansion, variable 36-month schedules, Total_Capital summation, Capital_Expenditures_In_Month[1] equals Total_Capital, Marketing_Total summation, Operations_Total summation, Payroll_Tax derivation, Rentable_Sqft computation, Total_Rental_Units ceiling rounding, monthly Rented_Units ramp under the default schedule, Rented_Sqft clamping, Gross_Revenue, Gross_Income equals Gross_Revenue when COGS is out of scope, Monthly_Depreciation, Loan_Proceeds equals Total_Capital minus Owner_Investment, Loan_Proceeds equals 0 when Owner_Investment exceeds Total_Capital, declining Monthly_Loan_Interest sequence, Monthly_Loan_Principal, Loan_Ending_Balance rolling forward, zero-interest loans, zero-proceeds loans, Income_Tax on positive months, zero Income_Tax on loss months, Total_Expenses, Net_Income, monthly cash roll-forward, Monthly_Depreciation add-back in cash flow, Capital_Expenditures_In_Month[1] in cash flow, principal-only loan servicing in cash flow, the Cash_Positive_Rule from Target_Cash_Positive_Month through Month 36, First_Sustained_Nonnegative_Month, Solver_Convergence at the minimum satisfying price, Solver's post-rounding verification pass, and a representative multi-month structural check modeled on the workbook.
3. WHERE the workbook and this specification disagree, THE test project SHALL derive expected values from this specification and SHALL treat the workbook only as structural guidance.
4. THE test project SHALL name tests descriptively so that the failing business rule is identifiable from the test name.
5. THE test project SHALL run to completion using `dotnet test` from the repository root without any external service dependency.

### Requirement 23: Terraform Scaffolding (No Provisioning)

**User Story:** As a developer, I want Terraform scaffolding for a future Cloud Run deployment that validates without provisioning, so that infrastructure evolves alongside the application without changing live resources.

#### Acceptance Criteria

1. THE Rehearsal_Forecast_Application SHALL include Terraform configuration under `infrastructure/terraform/modules/` and `infrastructure/terraform/environments/dev/`.
2. THE Terraform configuration SHALL declare required Terraform and provider versions and SHALL configure the Google provider with a variable-driven project ID, region, and service name.
3. THE Terraform configuration SHALL define a Cloud Run service, a container image input variable, a runtime service account, required IAM bindings, and a configurable public/restricted access setting.
4. THE Terraform configuration SHALL emit outputs including the service name and the service URL.
5. THE Terraform configuration SHALL include a `variables.tf` and an example `terraform.tfvars.example` file with sample values.
6. THE Terraform configuration SHALL document remote-state guidance in the infrastructure README without provisioning the remote-state bucket.
7. THE Terraform configuration SHALL apply labels and an environment name to each Cloud Run service resource for cost attribution and environment identification.
8. THE Terraform configuration SHALL NOT contain embedded credentials, project IDs, or secrets.
9. THE Terraform configuration SHALL pass `terraform fmt -check`, `terraform init -backend=false`, and `terraform validate` without provisioning any resources.
10. THE Rehearsal_Forecast_Application SHALL NOT invoke `terraform apply` from any script or workflow in this phase.

### Requirement 24: GitHub Actions CI Scaffolding

**User Story:** As a developer, I want a CI workflow that validates the .NET build, tests, and Terraform without deploying anything, so that every PR gets automated safety checks.

#### Acceptance Criteria

1. THE Rehearsal_Forecast_Application SHALL include a GitHub Actions workflow under `.github/workflows/` that runs on pull requests and pushes.
2. THE CI workflow SHALL restore .NET dependencies, build the solution, run the unit tests, publish the web application, and build the container image.
3. THE CI workflow SHALL run `terraform fmt -check`, `terraform init -backend=false`, and `terraform validate` against the Terraform configuration.
4. THE CI workflow SHALL upload build artifacts sufficient to reproduce the published web application and the container image reference.
5. THE CI workflow SHALL NOT run `terraform apply`, SHALL NOT deploy to Cloud Run, and SHALL NOT push the container image to any registry in this phase.
6. THE CI workflow SHALL document, in comments or in an accompanying markdown file, where future Google Cloud authentication and deployment steps would be added and SHALL indicate GitHub workload identity federation as the intended future authentication mechanism.
7. THE CI workflow SHALL not require cloud credentials for normal pull-request validation to succeed.
8. THE Rehearsal_Forecast_Application SHALL separate CI validation from any future deployment workflow such that adding deployment does not modify the CI validation workflow.

### Requirement 25: Multi-Stage Dockerfile

**User Story:** As a developer, I want a production-oriented Dockerfile that runs on Cloud Run when we choose to deploy, so that containerization is not a future scramble.

#### Acceptance Criteria

1. THE Rehearsal_Forecast_Application SHALL include a multi-stage Dockerfile that uses the .NET 10 SDK image for the build stage and the .NET 10 ASP.NET runtime image for the run stage.
2. THE Dockerfile SHALL configure the container to run as a non-root user where the base image supports it.
3. THE Dockerfile SHALL configure the container to listen on the port expected by Cloud Run (the `PORT` environment variable, defaulting to 8080).
4. THE Dockerfile SHALL be driven by environment-based configuration and SHALL NOT embed secrets, credentials, or environment-specific configuration values.
5. THE Rehearsal_Forecast_Application SHALL include a `.dockerignore` that excludes local build outputs, VS Code state, and test artifacts from the build context.

### Requirement 26: README Documentation

**User Story:** As a new developer joining the project, I want a README that explains the business purpose, the target-price meaning, formulas, sign conventions, and how to build, run, test, and export, so that I am productive on day one.

#### Acceptance Criteria

1. THE README SHALL describe the business purpose of the Rehearsal_Forecast_Application.
2. THE README SHALL explain the meaning of Flat_Price_Per_Sqft, distinguish it from Monthly_Price_Per_Sqft, and note the "flat / 36" derivation.
3. THE README SHALL document the financial formulas for Rentable_Sqft, Total_Rental_Units, Occupancy_Rate default schedule, Rented_Units, Rented_Sqft, Gross_Revenue, Marketing_Total, Operations_Total, Payroll_Tax, Monthly_Depreciation, Loan_Proceeds, Monthly_Loan_Interest, Monthly_Loan_Principal, Loan_Ending_Balance, Income_Tax, Total_Expenses, Net_Income, and Ending_Cash, and SHALL document sign conventions for the cash-flow forecast.
4. THE README SHALL describe how Constant_Mode and Variable_Mode work and how to switch between them.
5. THE README SHALL describe the project organization (Web, Core, Tests, Infrastructure, Workflows).
6. THE README SHALL describe how to install .NET 10 and how to open, restore, build, run, debug, and test the solution from VS Code and from the `dotnet` CLI.
7. THE README SHALL describe how to use the application (input flow, calculate action, results page, CSV export).
8. THE README SHALL describe the role of the workbook (`unitization-app/Rehearsal Studio Forcast 2.xlsx`) as structural guidance only.
9. THE README SHALL describe how to build the container image and how to run `terraform fmt -check`, `terraform init -backend=false`, and `terraform validate`.
10. THE README SHALL explain why deployment is disabled in this phase and outline how future GitHub Actions deployment could be enabled using workload identity federation.
11. THE README SHALL list the initial-phase limitations: no database, no authentication, no cloud provisioning, no capital scheduling, no COGS, no variable Owner_Withdrawals, Standard_Unit_Size fixed at 150.

### Requirement 27: Edge Case Handling

**User Story:** As a business planner, I want the application to handle common edge cases predictably rather than crashing or silently coercing values, so that unusual scenarios produce meaningful output or clear errors.

#### Acceptance Criteria

1. WHEN Total_Sqft is 0, THE Forecast_Calculator SHALL produce a forecast in which Rentable_Sqft, Total_Rental_Units, Rented_Units, Rented_Sqft, and Gross_Revenue are all 0 for every month.
2. WHEN Percentage_Available_For_Rent is 0, THE Forecast_Calculator SHALL produce a forecast in which Rentable_Sqft, Total_Rental_Units, Rented_Units, Rented_Sqft, and Gross_Revenue are all 0 for every month.
3. WHEN Total_Capital is 0 AND Owner_Investment is 0, THE Forecast_Calculator SHALL set Loan_Proceeds to 0 and SHALL treat the forecast as having no loan.
4. WHEN Income_Tax_Rate is 0, THE Forecast_Calculator SHALL set Income_Tax[m] to 0 for every m in [1, 36].
5. WHEN Owner_Withdrawals is 0, THE Forecast_Calculator SHALL not subtract any withdrawal in any month.
6. WHEN a Solver candidate satisfies the Cash_Positive_Rule before rounding but fails after being rounded UP to Currency_Precision, THE Solver SHALL raise the returned Flat_Price_Per_Sqft by additional units of Currency_Precision until the rule is satisfied post-rounding.
7. IF the Solver exceeds Solver_Safety_Limit without finding a price that satisfies the Cash_Positive_Rule, THEN THE Web_UI SHALL display a clear validation-style message identifying the solver-failure condition and SHALL NOT display a Flat_Price_Per_Sqft.
8. IF Target_Cash_Positive_Month equals 36, THEN THE Forecast_Calculator SHALL evaluate the Cash_Positive_Rule using only Ending_Cash[36] ≥ 0.
9. THE Forecast_Calculator SHALL NOT silently coerce invalid input values into zero, into defaults, or into any other value; invalid inputs SHALL be rejected by the Input_Validator per Requirement 2.

### Requirement 28: Runtime Independence from Excel

**User Story:** As an operator, I want the application to run without any dependency on Microsoft Excel or Office automation, so that it can be hosted in a cloud container.

#### Acceptance Criteria

1. THE Rehearsal_Forecast_Application SHALL NOT invoke Excel, Office automation libraries, or any Excel COM interop at runtime.
2. THE Rehearsal_Forecast_Application SHALL NOT require the workbook file to be present at runtime.
3. WHERE the workbook is referenced by the test suite as structural guidance, THE test project SHALL NOT load the workbook file at test runtime.

### Requirement 29: Definition of Done

**User Story:** As a project owner, I want a single checklist that summarizes what "phase 1 complete" means, so that I can confirm we've hit the bar before moving on.

#### Acceptance Criteria

1. THE Rehearsal_Forecast_Application SHALL restore and build under .NET 10 using `dotnet build` from the repository root.
2. THE Rehearsal_Forecast_Application SHALL run from VS Code and from `dotnet run` in the `src/RehearsalForecast.Web/` project directory.
3. THE Web_UI SHALL accept all required financial inputs across the input sections defined in Requirement 17.
4. WHERE an input supports Constant_Mode and Variable_Mode, THE Web_UI SHALL support both modes per Requirement 1.
5. THE Forecast_Calculator SHALL produce a complete 36-month forecast including Rentable_Sqft, Total_Rental_Units, occupancy, revenue, expenses, loan schedule, income tax, and cash flow.
6. THE Forecast_Calculator SHALL compute Loan_Proceeds as Total_Capital minus Owner_Investment (floored at zero) per Requirement 10.
7. THE Forecast_Calculator SHALL record Capital_Expenditures_In_Month[1] as Total_Capital per Requirement 9.
8. THE Loan_Calculator SHALL produce a declining Monthly_Loan_Interest sequence and SHALL cause the cash-flow forecast to subtract only Monthly_Loan_Principal per Requirement 11.
9. THE Forecast_Calculator SHALL apply Income_Tax only to positive Pre_Tax_Income months per Requirement 12.
10. THE Solver SHALL find the minimum Flat_Price_Per_Sqft satisfying the Cash_Positive_Rule and SHALL round UP to Currency_Precision and re-verify per Requirement 15.
11. THE Web_UI SHALL display the results including the complete 36-month forecast table per Requirement 16.
12. THE CSV_Exporter SHALL export all 36 monthly records per Requirement 18.
13. THE test suite SHALL pass under `dotnet test` covering the topics enumerated in Requirement 22.
14. THE Dockerfile SHALL build successfully when Docker is available.
15. THE Terraform configuration SHALL pass `terraform fmt -check`, `terraform init -backend=false`, and `terraform validate` without provisioning resources.
16. THE GitHub Actions workflow SHALL perform CI validation only and SHALL NOT deploy or provision.
17. THE README SHALL enable a developer unfamiliar with the stack to install .NET 10, open the solution, run the application, run the tests, build the container image, and validate Terraform.
18. THE Rehearsal_Forecast_Application SHALL NOT introduce a database, authentication, or cloud provisioning in this phase.
