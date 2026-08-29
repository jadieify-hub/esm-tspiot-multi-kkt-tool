# ESM TS PIoT Second KKT Tool Design

## Goal

Build a simple Windows desktop utility that adds and registers a second KKT in ESM/TS PIoT through the local HTTP API, without using PowerShell inside the application.

## Target Builds

The project ships two desktop builds from one shared behavior set:

- Legacy build: WinForms on .NET Framework 4.8, intended for Windows 7 SP1 through Windows 11, x86 and x64.
- Modern build: WinForms on .NET 8, intended for Windows 10/11, published self-contained for win-x86 and win-x64.

The Legacy build is not strictly self-contained because .NET Framework 4.8 must be installed on the PC. The distribution notes must mention this and may include a link or offline installer guidance. The Modern build should not require a preinstalled .NET runtime.

## User Workflow

The form contains five groups:

- Connection to ESM/TS PIoT: editable service base URL, default `http://127.0.0.1:51077`.
- Second KKT data: second KKT serial number, FN serial number, owner INN.
- Ports: `port` default `50402`, `softPort` default `51402`, `dkktPort` default `4042` (ESM DKKt orchestrator; ATOL KKM service uses `4041`).
- Actions: check current KKT instances, fill second KKT data, add second instance, register second KKT, clear log.
- Execution log: large read-only text area with timestamped request, response, and error details.

Before PUT registration, the user must explicitly check:

`Я проверил связь в драйвере АТОЛ именно со второй физической ККТ`

The application must not automate ATOL driver settings. That step remains manual and user-confirmed.

## API Behavior

The utility calls these endpoints relative to the entered base URL:

- `GET /api/v1/instances/info`
- `POST /api/v1/tspiot`
- `PUT /api/v1/tspiot`

POST body:

```json
{
  "id": "second KKT serial",
  "port": 50402,
  "softPort": 51402,
  "dkktPort": 4042
}
```

PUT body:

```json
{
  "id": "second KKT serial",
  "kktSerial": "second KKT serial",
  "fnSerial": "second FN serial",
  "kktInn": "owner INN"
}
```

`id` and `kktSerial` are always the same serial number and are kept as strings to preserve leading zeroes.

## Validation

Before POST and PUT:

- Base URL must be non-empty and an absolute HTTP or HTTPS URL.
- KKT serial must be non-empty and digits only.
- FN serial must be non-empty and digits only.
- INN must be digits only and length 10 or 12.
- Ports must be integers in `1..65535`.
- `port` and `softPort` must differ.

Before POST, the application should call GET and check whether an instance with the entered `id` already exists. If it does, show a warning that repeated addition may cause error 1010. The user may still continue after confirmation.

Before PUT, the ATOL connection confirmation checkbox is required.

## Error Handling

The log and user-facing messages must decode these common errors:

- `1010`: service with this id already exists; the KKT may already be added.
- `1001`: invalid request body; check entered fields.
- `1015`: DKKt service chain is not running; check ATOL/orchestrator services and `dkktPort`, normally `4042` for the ESM orchestrator (`4041` is the ATOL KKM service port).
- `2046`: ESM service is not registered or not running; also check driver connection with the second KKT.
- HTTP `403`: check registration, certificates, access rights, and ESM/TS PIoT state.
- Connection failure: check that the ESM/TS PIoT service is running and the address is correct.

Unknown errors are logged with the raw HTTP status and response body.

## Architecture

Keep business behavior shared and UI-specific code thin:

- `EsmTspiot.Shared`: .NET Standard 2.0 class library for models, validation, endpoint construction, response formatting, error decoding, and HTTP service abstractions.
- `EsmTspiot.Legacy.WinForms`: .NET Framework 4.8 WinForms app using the shared library.
- `EsmTspiot.Modern.WinForms`: .NET 8 WinForms app using the shared library.
- `EsmTspiot.Shared.Tests`: tests for validation, URL building, error decoding, and instance parsing.

The shared library must avoid APIs unavailable on .NET Framework 4.8 through .NET Standard 2.0.

## Testing

Automated tests cover shared validation and parsing. Manual verification covers both UI projects:

- buttons disable while an async request is running and re-enable after completion;
- full process stops at the first failing step;
- PUT cannot run without the ATOL confirmation checkbox;
- POST duplicate-id warning can be confirmed or cancelled;
- logs include method, URL, request body when present, status, response body, and decoded error message.

## Documentation

`README.md` must explain:

- what the utility does;
- which build to use for old and new Windows versions;
- how to fill in the fields;
- recommended operation order;
- what `port`, `softPort`, and `dkktPort` mean;
- common errors;
- build and publish commands.
