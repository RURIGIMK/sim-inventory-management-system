# Sprint III Completion Plan

## Goal

Deliver a coherent, tested and publicly demonstrable academic prototype. Prioritise approved objectives and working user tasks over additional screens or infrastructure. Use synthetic data only.

## Workstream 1: Stabilise the existing system

1. Keep Google sign-in and administrator-approved account access. Do not reintroduce email OTP.
2. Verify the bootstrap administrator flow, active/disabled account handling and API-enforced roles.
3. Complete end-to-end checks for SIM registration, employee status, allocation, return/reassignment and audit verification.
4. Fix important defects and add automated tests for the rules most likely to regress.
5. Keep the database bootstrap limitation visible. Reviewed EF Core migrations and a backup/restore drill remain production hardening, not a reason to pretend the prototype is production-ready.

## Workstream 2: Add a small, explainable intelligence capability

Start with deterministic rules using fields the system already stores. Candidate alerts:
- A SIM remains assigned to an employee who is inactive or terminated. The API already blocks this transition, so the alert is most useful for identifying legacy or inconsistent records and should not duplicate a rule that is already reliably enforced.
- A monthly SIM cost is unusually high compared with a suitable peer baseline, such as other SIMs on the same operator, only when there are enough peer records to make the comparison meaningful.
- A record needs an administrative review because of a clearly defined asset-control condition.

Each alert should show the affected record, the rule that triggered it, the values used and a suggested human review action. Do not show an arbitrary confidence percentage, call a rule an AI prediction, or automatically disable a SIM or change a user's access.

### Optional ML experiment

Use the existing Colab notebook and synthetic data. Compare a transparent baseline, such as a robust median/MAD threshold, against Isolation Forest only if the data and experiment justify it. Use a fixed seed and documented assumptions. Keep injected anomalies and held-out evaluation records separate from the data used to tune the method. Record precision, recall, false-positive rate and example errors.

Do not add model serving to the deployed application unless the experiment demonstrates measurable value and the integration can be completed and tested within the sprint. A documented, reproducible experiment can be reported as a prototype; it must not be described as a live application feature unless it is actually integrated.

## Workstream 3: Deployment

Target the existing zero-cash-cost plan:
- Neon PostgreSQL for hosted persistence.
- Render Free for the .NET API.
- Cloudflare Pages Free for the static frontend.
- GitHub Actions for CI and gated deployment.

Create provider resources only after code checks and configuration are ready. Keep `DEPLOY_ENABLED=false` until all required secrets and variables are configured. Use synthetic data. Test the deployed login, API connectivity, core workflows, failure messages and audit verification. Document cold starts and free-tier limitations.

## Workstream 4: Evaluation and report

1. Link each approved objective to a feature, test/evaluation method, observed result and evidence file.
2. Record actual expected and actual outcomes for the Sprint III acceptance matrix in `docs/TESTING.md`.
3. Capture dated screenshots and GitHub Actions run links. Do not mark a pending test as passed.
4. Update Chapter 4 diagrams only where the implemented architecture differs from the design.
5. Write Chapter 5 from implemented functionality and observed tests. Report the intelligence experiment separately from the application unless integrated.
6. Write Chapter 6 using the evidence in Chapter 5, including limitations and future work.
7. Prepare a reproducible handover: setup instructions, environment-variable example without secrets, test instructions, deployed URLs, known limitations and a release commit/tag.

## Definition of done

- The approved core workflow works from sign-in through inventory/allocation and audit review.
- API authorization is tested independently of frontend visibility.
- Important validation and failure paths have recorded outcomes.
- The live prototype is accessible over HTTPS and uses the hosted database.
- Any intelligence result is explainable, evaluated and labelled according to what is actually implemented.
- Chapters 5 and 6, test evidence and handover instructions match the code and deployment.
- Remaining limitations are explicit. Deployment alone does not constitute project completion.
