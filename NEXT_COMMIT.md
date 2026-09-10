# Next Commit

## Subject

`feat(client): add request metadata and streaming multipart transfers`

## Body

- preserve the existing request ID, OPX error-body parsing, device metadata,
  sample, and WebSocket test changes in the current worktree;
- add retry-safe multipart form/file contracts using fresh readable stream
  factories;
- add `PostDownloadAsync` to stream multipart uploads and binary responses
  without Base64 or whole-file memory buffering;
- preserve bearer-token refresh, request ID reuse, HTTP version policy, custom
  headers, cancellation, and streaming download progress;
- document the multipart conversion workflow and bump `Opx.Api.Client` to
  version 1.0.13.

## Verification

- Release test run passed 27 tests, with 2 explicit stress tests skipped;
- the multipart test verifies POST method, form fields, filename, content type,
  bearer token, binary response streaming, and destination bytes;
- local Release package `Opx.Api.Client.1.0.13.nupkg` was created for consumer
  validation only; it was not published.

No commit, push, package publication, or deployment is included.
