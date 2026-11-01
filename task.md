Requesting changes.

I tested this PR head in disposable Docker and isolated runners. The PostgreSQL happy path for import, approval, and attach passed, but a synchronized import probe persisted two identical bank items, and an existing-content duplicate returned a generic conflict instead of row-specific validation. Ten service tests passed in an isolated runner after supplying a temporary namespace shim; the repository test file itself is missing that namespace import. The full solution test run is blocked by compile errors in the merged interview sources.

The review also includes the Ponytail finding that list requests load and return the full revision history for every item. CI currently has no PostgreSQL/Keycloak integration or E2E job, as required by TASK-K. Please address the F1 findings and coordinate the shared interview build blockers and E2E coverage with their owners.

/src/Modules/AccessConfig/AiOralExam.Modules.AccessConfig/Application/QuestionBank/QuestionBankContracts.cs lines 73-76
[P1] Enforce duplicate uniqueness in the database. This read and the later insert are separate operations, and the schema has no unique key for course plus normalized content. I reproduced two synchronized PostgreSQL imports both passing this check and persisting duplicate items. Please add a database-enforced uniqueness rule and map its violation to a conflict so concurrent requests cannot bypass this check.

src/Modules/AccessConfig/AiOralExam.Modules.AccessConfig/Application/QuestionBank/QuestionBankContracts.cs lines 76-79
[P2] Return row-specific errors for existing duplicates. Duplicates within the submitted batch report items[i].content, but a match against existing bank content returns one generic conflict here. The import UI cannot identify the offending row, which misses the F1-B row-error contract. Collect matching row indices and return field-path validation details.

src/Modules/AccessConfig/AiOralExam.Modules.AccessConfig/Application/QuestionBank/QuestionBankContracts.cs lines 18-21
[P2] Keep revision history out of list responses. This eagerly loads every revision for every item, and ToDto includes revision content and provenance. As history grows, each list refresh loads and sends the full history; the detail route can fetch it on demand. Use a compact list projection and load revisions in GetAsync.

tests/AiOralExam.UnitTests/ReportCalculatorTests.cs lines 275-278
[P1] Import the namespace for ExamSessionStatus. The enum is declared in AiOralExam.Modules.AccessConfig.Contracts, but this file imports only the Domain namespace. The isolated build failed with CS0103 here; add the missing using so the test project compiles.

.github/workflows/backend-ci.yml lines 24-27
[P2] Add the planned PostgreSQL/Keycloak integration job. This workflow only runs dotnet test, while the HTTP test factory substitutes EF InMemory and fake Keycloak. TASK-K calls for real PostgreSQL/Keycloak integration or E2E coverage, so the current CI cannot verify that contract.

Backend CI failure report:

This job failed because the test run hit a runtime exception in EF Core/LINQ rather than a normal application/business-logic failure.

The key evidence from the logs is this recurring error:

- System.InvalidOperationException: An exception was thrown while attempting to evaluate a LINQ query parameter expression
- Inner error: GenericArguments[1], 'System.ReadOnlySpan`1[System.Guid]' / 'System.ReadOnlySpan`1[System.Int32]' violates the constraint of type 'TRet'
This indicates a query is building a LINQ expression that includes a ReadOnlySpan value, and EF Core is trying to parameterize/evaluate it and crashes while creating a generic expression. That is why multiple tests fail with the same low-level exception instead of the expected domain exceptions.

The failing tests show the impact clearly:

- Unit tests expected ConflictException or ValidationException, but actual exception was InvalidOperationException
- Integration tests returned InternalServerError / wrong HTTP status because the service failed before reaching the intended validation or conflict handling
Summary of the failing counts from the job log:

- Unit tests: 5 failed, 315 passed, 3 skipped
- Integration tests: 3 failed, 41 passed
- Overall: failed with exit code 1
So the root cause is not “one broken endpoint” but a shared LINQ/EF query issue triggered by ReadOnlySpan-based values in the query pipeline, which breaks several flows and causes the suite to fail.

## Source-checked resolution plan

The steps below are based on the current checkout. Keep the report above as evidence of the tested PR head; where the checkout differs from the report, verify against the exact branch and CI logs before making an additional change.

### 1. Make question-bank content unique under concurrent writes (P1)

**Current state:** `QuestionBankService.ImportAsync` reads existing course content, compares trimmed strings case-insensitively, and later inserts. There is no matching unique constraint in `database/01_schema.sql`, `database/07_question_bank.sql`, or the EF mapping in `AccessConfigDbContext`. Two requests can therefore pass the read before either writes.

**Changes and ownership:**

1. Agree on one canonical content-equivalence rule matching the current `NormalizeContent` (`Trim`) and `StringComparer.OrdinalIgnoreCase` behavior. The database rule and every application precheck must use equivalent normalization and case folding; do not assume PostgreSQL `lower()` and .NET ordinal case-insensitive comparison are identical for every Unicode string.
2. Coordinate the schema change with the database owner (TASK-K does not own `database/`). Add database-enforced uniqueness scoped to `course_id` and canonical content in the schema source of truth, `database/01_schema.sql`, and add a forward-only upgrade migration after the current migration sequence. Use a named constraint/index so the application can identify this particular violation.
3. Before applying the upgrade, detect and report existing collisions using the agreed normalization. Resolve them explicitly with the data owner; do not silently delete or rewrite existing bank items. The migration must fail clearly if collisions remain.
4. In the service, translate only the matching PostgreSQL unique-violation (`23505` and the named constraint) from `DbUpdateException` into the existing `question_bank_duplicate` `ConflictException`. Follow the specific-filter pattern in `AiConfigService`; do not turn unrelated database failures into conflicts. Apply this handling to every write path capable of creating or changing content (import, manual create, AI drafts, and Draft update), not just imports.
5. Keep the precheck for a friendly response, but treat the database constraint as the final concurrency guard. A concurrent loser must return the documented conflict response, and an atomic import must not leave a partial batch.

**Verification:** add a PostgreSQL-backed test that synchronizes two imports of the same normalized content into one course and proves exactly one persists and the other receives the expected conflict. Also test case/whitespace variants, same content in different courses, named-constraint error mapping, and that no unrelated unique violation is mislabeled. InMemory tests alone cannot establish database uniqueness or concurrent behavior.

### 2. Report existing-content duplicates by import row (P2)

**Current state:** duplicates within a submitted batch already produce `items[i].content` validation details. A duplicate found in the course-wide query currently throws one generic `ConflictException`.

**Changes:**

1. Build the normalized existing-content set as today, then walk the submitted rows with their indices and add a validation entry for every existing match, using the existing batch convention: `items[i].content`.
2. Throw the standard `ValidationException` with all matching row paths before creating entities, audit rows, or revisions. Keep the database-race conflict from step 1 as a conflict; it cannot reliably be converted into a row-specific precheck after the race.
3. Extend unit and HTTP contract tests for one and multiple matches against existing records, case/trim normalization, and atomic no-write behavior. Assert the response uses the standard validation status/code and contains each offending field path.

### 3. Keep revision history out of bank list responses (P2)

**Current state:** `ListAsync` uses `Include(x => x.Revisions)` and returns `QuestionBankItemDto`; `ToDto` serializes every revision’s content and provenance. The detail path already loads revisions through `GetItemAsync`.

**Changes:**

1. Add a compact list-response DTO without the `Revisions` collection, and update `IQuestionBankService.ListAsync` and `QuestionBankController.List` to use it. Do not return an empty revision list as if it were the item's history.
2. Replace the list `Include`/entity materialization with a database projection containing only fields needed by the list. Preserve course authorization and status filtering, ordering, and existing list semantics.
3. Keep the detail response and `GetAsync` revision loading intact. Update API contract tests and any generated/API client types that consume the list response; create/import/update/moderation responses can retain their existing detail shape unless the API contract is intentionally changed separately.
4. Test that list JSON omits revision content and provenance and that detail JSON still returns ordered revision history.

### 4. ReportCalculator test namespace finding (P1)

The current `tests/AiOralExam.UnitTests/ReportCalculatorTests.cs` already has `using AiOralExam.Modules.AccessConfig.Contracts;`, which is the namespace identified by the report. Therefore, the requested missing-using change appears already present in this checkout; do not add a duplicate import. Build the unit-test project on the exact branch under review and confirm `ExamSessionStatus` resolves. If CI still reports CS0103, compare its source revision and compiler output with this checkout and restore the import only on the branch where it is actually absent.

### 5. Add real PostgreSQL/Keycloak verification to CI (P2 / TASK-K)

**Current state:** `.github/workflows/backend-ci.yml` only restores and runs `dotnet test`. `ApiTestFactory` replaces both application DbContexts with EF InMemory and replaces `IKeycloakAdminClient` with a fake, so its HTTP contract tests do not verify PostgreSQL or real Keycloak behavior. `docker-compose.yml` and `keycloak/aives-realm.json` provide a local PostgreSQL/Keycloak starting point, but local development defaults are not production credentials.

**Changes and ownership:**

1. Keep fast unit/in-memory tests, and add a separately identifiable CI integration/E2E job using isolated, ephemeral PostgreSQL and Keycloak services. Wait for health checks before starting tests; configure the API/test process to use those services rather than `ApiTestFactory`'s fake registrations.
2. Apply the approved schema and forward migrations to the disposable test database in the documented order, then seed only test data. Exercise the real Npgsql provider, including the uniqueness/race scenario from step 1.
3. Add a real-token authorization smoke test against the imported test realm and the required TASK-K workflow coverage: publish, interview text/voice paths where supported, lecturer review, student feedback, and reporting/statistics/CSV. Include permission-denied and relevant retry/timeout cases. Use an OIDC flow supported by the realm (the repository disables password grants for public clients); do not weaken realm settings just to make CI convenient.
4. Keep credentials test-only and ephemeral; use GitHub Actions secrets only for values that cannot be safely generated at job runtime. Do not copy local development passwords or client secrets into workflow files or logs.
5. Preserve the existing fast CI job and make the new job's failures attributable to infrastructure startup, schema setup, auth, or a specific contract test.

**Verification:** run the new workflow from a clean database/realm state and demonstrate that it fails when PostgreSQL or Keycloak is unavailable or when the tested auth/schema contract is broken. Existing in-memory tests should continue to run independently.

### 6. Diagnose and fix the EF Core/LINQ `ReadOnlySpan` runtime exception

The supplied failure summary does not name the failing methods or include stack frames identifying the offending expression. A source scan of this checkout found no explicit `ReadOnlySpan` in the application LINQ queries, so the exact expression is not established by the report alone. Do not make a broad rewrite or suppress the `InvalidOperationException`.

1. Retrieve the complete CI stack trace and exact failed test names from the failed run. For each failure, identify the service query and the parameter expression EF Core is evaluating.
2. Inspect the expression's captured arguments and overload resolution for span-backed values or collection expressions. Source query sites using collection `Contains` include `QuestionBankService`, `ExamConfigurationApi`, `ExamSessionQueries`, `ParticipantService`, `QuestionService`, `MaterialService`, and `InterviewResultsApi`; these are investigation leads, not confirmed causes.
3. Where an offending query captures a span or a value inferred as a span, materialize/declare a concrete array or list of the expected element type before constructing the EF expression and pass that parameter to the query. Keep span operations outside EF-translated expressions. Do not change unrelated collection queries unless the trace/reproduction proves they are involved.
4. Add a regression test using the same EF Core/Npgsql version and provider path that triggered the failure. Assert the intended domain exception or HTTP status, not merely that the query no longer throws. Cover both `Guid` and `int` parameters if both forms appear in the failing traces.
5. Re-run the exact previously failing tests and the full relevant suite; verify there are no `InvalidOperationException` query-parameter failures or success-shaped fallback responses.

### 7. Coordinate the merged Interview build blockers with their owners

The report says the full solution is blocked by compile errors in merged Interview sources but gives no compiler diagnostics or file/line list. TASK-K explicitly assigns F3 implementation/compiler fixes to Minh/Phuc, with Khang coordinating integration and reporting failures. Send the exact CI compiler output and branch/commit to the responsible owner, have that owner fix the F3 source, then rebase/re-run the solution build. Do not guess at or edit Interview code without the specific compiler errors and ownership agreement.

### Completion order and acceptance checks

1. Resolve the specific F3 compile blockers with their owners so full-solution validation is possible.
2. Implement the content normalization/constraint with the schema owner, then add narrow PostgreSQL tests for race handling and atomicity.
3. Implement row-indexed existing-duplicate validation and compact list DTO/projection; update unit and HTTP contracts.
4. Investigate the EF parameter-evaluation failure using full logs and add a provider-backed regression test for the proven cause.
5. Add and run the real PostgreSQL/Keycloak CI job required by TASK-K.
6. Run targeted QuestionBank and report-calculator tests, then `dotnet test AiOralExam.sln --configuration Release`; record the exact commands and results. Acceptance requires no duplicate persistence under concurrent import, field-path validation for existing import duplicates, no revision history in list payloads, detail history preserved, and all relevant CI jobs green.

## Implementation status (2026-11-01)

- Implemented the generated PostgreSQL `lower(btrim(content))` key, named course-scoped unique index, forward-only migration with existing-collision diagnostics, and specific unique-violation-to-conflict mapping for create, import, AI draft creation, and Draft update.
- Existing-content import duplicates now return `items[i].content` validation details; the list endpoint now uses a compact projection/DTO while detail still includes revision history.
- Added unit/HTTP contract coverage, opt-in PostgreSQL concurrency and conflict-mapping tests, and a CI job that provisions PostgreSQL, exercises the upgrade/concurrency tests, and validates a Keycloak-issued token against a PostgreSQL-backed API endpoint. The smoke test uses a CI-created service account and leaves the public clients' password grants disabled.
- The namespace import reported missing is already present in this checkout. `dotnet test AiOralExam.sln --configuration Release --no-restore` passes here (321 unit tests and 46 integration tests; five opt-in PostgreSQL concurrency tests skipped because no disposable database was configured). The reported ReadOnlySpan EF failure and Interview compile blockers did not reproduce, so no unrelated query or Interview code was changed.
- The CI authentication smoke test does not cover the full TASK-K publish-to-report E2E flow across frontend/voice/review/reporting; coordinate that broader flow with the frontend and F3 owners. The new CI job has not been run from this local environment.
