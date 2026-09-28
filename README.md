# SIM Inventory Management System

## An Integrated Framework for Proactive SIM Inventory Management, Allocation, Tracking, and Audit Compliance

A web-based information system proposed for organizational management of Subscriber Identity Module (SIM) assets. The project focuses on centralized SIM inventory, allocation tracking, role-based access control, auditability, and reporting.

> Project status: Academic capstone project under development. The repository is being prepared incrementally alongside implementation.

## Project Context

Organizations may manage SIM cards through spreadsheets, paper records, or disconnected processes. This project proposes a centralized web application that provides a structured record of SIM assets and their lifecycle.

The system is designed around three main layers:

1. Presentation layer: Next.js and TypeScript
2. Application layer: ASP.NET Core Web API
3. Data layer: PostgreSQL

## Planned Core Features

- User authentication and role-based access control
- SIM registration and inventory management
- SIM allocation, reassignment, return, and deactivation
- Allocation history
- Audit logging of significant state-changing actions
- Dashboard summaries and operational reporting
- User and role administration
- Filtering and export of management information
- Database-backed validation and integrity constraints

## Planned User Roles

| Role | Purpose |
| --- | --- |
| Administrator | User management, SIM administration, allocation and audit oversight |
| Departmental Manager | SIM allocation, reassignment, operational management and reporting |
| Viewer | Read-only access to permitted inventory and reporting information |

## Technology Stack

| Area | Technology |
| --- | --- |
| Frontend | Next.js, TypeScript, Tailwind CSS |
| Backend | ASP.NET Core Web API, C# |
| ORM | Entity Framework Core |
| Database | PostgreSQL |
| API testing | Postman |
| Backend testing | xUnit |
| Frontend testing | Jest, React Testing Library |
| Version control | Git and GitHub |
| CI | GitHub Actions |

## Data / Dataset

This is an information-system project rather than a machine-learning project, so there is no model-training dataset.

Development uses synthetic test/seed records representing SIM inventory, operators, users, allocations and audit activity. Synthetic data is appropriate for development because real employee identifiers, phone numbers and SIM credentials should not be committed to a public repository.

The sample data is documented under the data directory.

## Repository Structure

The intended structure is:

    sim-inventory-management-system/
    ├── .github/
    │   └── workflows/
    │       └── ci.yml
    ├── data/
    │   ├── README.md
    │   └── sample_sims.csv
    ├── docs/
    │   ├── AI_ASSISTANCE.md
    │   └── DEVELOPMENT.md
    ├── src/
    │   ├── backend/
    │   └── frontend/
    ├── tests/
    ├── .gitignore
    └── README.md

The application source will be added as implementation progresses. The CI workflow is deliberately written to support the planned backend/frontend structure without claiming that components already exist.

## Development Workflow

The project uses Git for version control. Changes should be committed in small, meaningful units.

Example commit sequence:

    docs: add project README
    ci: add GitHub Actions validation workflow
    data: add synthetic SIM seed dataset
    docs: document development workflow
    feat: implement SIM registration API
    feat: implement SIM allocation workflow
    test: add allocation service tests

This makes the development history easier to inspect and explain during project review.

## Continuous Integration

GitHub Actions is configured in .github/workflows/ci.yml.

The pipeline is intended to:

1. Trigger on pushes and pull requests.
2. Restore and build the .NET backend when the backend project exists.
3. Install and validate the Next.js frontend when the frontend project exists.
4. Run automated tests when the corresponding test projects/scripts exist.
5. Fail the workflow when a configured build or test step fails.

This is CI, not machine-learning automation. The project does not currently require an ML pipeline because machine-learning model training is outside the stated project scope.

## Security and Data Handling

- Do not commit real employee personal information.
- Do not commit real SIM credentials, secrets, database passwords or API keys.
- Store environment-specific secrets using GitHub Actions secrets or local environment variables.
- Passwords must be stored using secure password hashing through the chosen authentication framework.
- Authorization should be enforced by the backend rather than relying only on frontend visibility.

## Academic Alignment

The repository supports the project described in the proposal and Chapters 1–3:

- Problem: fragmented/manual SIM asset management.
- Objective: develop a web-based system for SIM tracking, allocation and audit.
- Methodology: iterative/prototyping-oriented development.
- Architecture: three-tier web architecture.
- Evaluation: functional, integration, system and black-box testing.
- Deliverables: web application, source repository, database schema, evaluation evidence and documentation.

## Current Limitation

The GitHub repository is being populated incrementally. A README, CI foundation and development/data documentation do not by themselves constitute the completed application. Implementation evidence should only be described as completed after the corresponding source code, tests and commits exist in the repository.
