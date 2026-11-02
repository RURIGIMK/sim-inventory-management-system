# Reporting and evaluation

## Application reports

The authenticated reporting endpoint is `GET /api/reports/summary`. It reads the current application database and returns:

- Inventory totals by SIM status and monthly committed cost.
- Employee directory totals by active and inactive/terminated status.
- Active, historical and returned allocation counts.
- Contracts that are overdue or end within the next 30 days.
- SIM count, allocated/available count and monthly cost grouped by operator.
- The same inventory and cost breakdown grouped by cost centre.
- The latest 100 allocation records, including returned assignments.

The Reports page presents these aggregates and provides a CSV export containing the summary, operator and cost-centre breakdowns, and latest allocation history. The endpoint requires an authenticated session. The report is a current-state operational snapshot, not a historical month-by-month financial statement.

## Interpretation and limitations

- Monthly committed cost is the sum of recorded monthly SIM costs for all SIMs except those marked Deactivated. It is not an invoice total and does not include billing adjustments, taxes or usage charges unless represented in the SIM monthly-cost field.
- Cost-centre and operator groupings are based on the values currently stored on each SIM.
- Allocation history is limited to the latest 100 records in the page and CSV.
- Contract counts use the API server's UTC date.
- Reports use the synthetic demo records in this academic prototype. They must not be interpreted as actual organizational expenditure or employee data.
- The CSV is generated in the browser from the authenticated report response. It contains no credentials or access tokens.

## Asset intelligence and Google Colab

The application intelligence page uses deterministic, explainable rules implemented in the .NET API. It flags contract dates, SIM/assignment inconsistencies, assignments to inactive employees and unusual costs relative to same-operator peers. It is not a trained machine-learning model.

The Google Colab notebook is a separate synthetic-data experiment. It compares a cost-ratio baseline with Isolation Forest on held-out synthetic records and exports a CSV for review. The notebook does not connect to the deployed database and its predictions are not consumed by the application. The Colab results must be run and evaluated before any model-performance claim is made.

A future integration would require an approved data pipeline, a repeatable model evaluation, versioned model/feature definitions, monitoring and a human review workflow. Do not expose Colab credentials or real employee, phone, ICCID or operator billing data in the notebook.
