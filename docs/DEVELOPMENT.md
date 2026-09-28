# Development Guide

## Development Approach

The project follows an iterative prototyping approach. Work is divided into small increments so that requirements, implementation and testing can be refined as the system develops.

## Suggested Implementation Order

1. Repository and project structure
2. Database schema and migrations
3. Authentication and authorization
4. SIM inventory CRUD
5. Allocation and reassignment
6. Audit logging
7. Dashboard and reporting
8. User management
9. Automated tests
10. Deployment and final evaluation

## Git Workflow

Each meaningful change should produce a focused commit. Avoid large commits that mix unrelated features.

Examples:

- chore: initialize project structure
- docs: add project documentation
- ci: add GitHub Actions workflow
- feat: add SIM registration
- feat: add SIM allocation
- feat: add audit logging
- test: add SIM allocation tests
- fix: prevent duplicate SIM allocation

## CI/CD Distinction

The current workflow is continuous integration. It automatically validates source changes through build, lint and test steps when the relevant application files exist.

Continuous deployment should only be added once a deployment target has been selected and configured with the required secrets. It should not be described as implemented unless a successful deployment workflow exists.

## Questions the Implementation Should Answer

For a code review, the developer should be able to demonstrate:

- Where SIM records are created and validated.
- How duplicate ICCIDs or phone numbers are prevented.
- How allocation changes SIM status.
- How reassignment preserves allocation history.
- How unauthorized roles are blocked.
- Where audit records are created.
- How reports obtain their data.
- How database relationships are enforced.
- Which automated tests cover important business rules.
