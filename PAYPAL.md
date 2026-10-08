# PayPal Checkout

The backend uses PayPal Orders API v2. Credentials are read only from environment variables and are never returned to the browser.

## Environment

Required variables:

- `PAYPAL_ENVIRONMENT`: `Sandbox` or `Live`.
- `PUBLIC_PP_CLIENT`: the REST app client ID for the selected environment.
- `PP_KEY_SECRET`: the matching private REST app secret.
- `CORS_ALLOWED_ORIGINS`: semicolon-separated exact frontend origins, for example `http://localhost:5173;https://shop.example.com`.
- `ConnectionStrings__DefaultConnection`: PostgreSQL connection string for the database.

Create a REST app in the PayPal Developer Dashboard and use Sandbox credentials for development. Set these values in the deployment environment or local shell; never commit credentials or put the secret in frontend configuration. Switch to `Live` only with the production app credentials. The API chooses `api-m.sandbox.paypal.com` or `api-m.paypal.com` from `PAYPAL_ENVIRONMENT`.

For a local fish shell session, configure values directly in the shell without committing them:

```fish
set -x PAYPAL_ENVIRONMENT Sandbox
set -x PUBLIC_PP_CLIENT 'your-sandbox-client-id'
set -x PP_KEY_SECRET 'your-sandbox-client-secret'
set -x CORS_ALLOWED_ORIGINS 'http://localhost:5173'
set -x ConnectionStrings__DefaultConnection 'your-postgresql-connection-string'
dotnet run
```

Apply the `20261007120000_AddPayPalCheckouts` migration using the deployment's EF Core migration process before serving checkout requests.

## Authentication and frontend contract

This repository currently has no configured HTTP authentication scheme, and its GraphQL `login` mutation does not issue a token. PayPal routes intentionally reject unauthenticated requests with `401`; they require an already-validated principal with a positive user ID in `sub`, `NameIdentifier`, or `usuarioId`. Add the project's actual authentication provider/token validation before exposing these routes. The frontend must send its authenticated `Authorization` header to both PayPal routes and `crearPedido`. `usuarioId` remains in the create body for compatibility but must match the authenticated identity; it is never used as proof of identity.

Create request: `{ "usuarioId": 7, "items": [{ "productoId": 1, "cantidad": 1 }] }`. Capture request: `{ "orderId": "..." }`. Capture returns `status: "COMPLETED"` only after the API confirms the capture and the stored amount and currency match.

After capture, keep calling GraphQL `crearPedido`, adding `paypalOrderId` to its input and retaining `usuarioId` and `items`. The existing `paymentId` remains supported for Mercado Pago. For example, the PayPal path can omit `paymentId`:

```graphql
mutation {
  crearPedido(input: {
    usuarioId: 7,
    items: [{ productoId: 1, cantidad: 1 }],
    paypalOrderId: "PAYPAL_ORDER_ID"
  }) { id total status }
}
```

The order amount is calculated from database prices in MXN. Shipping is free when merchandise reaches $500 MXN; otherwise it is $99 MXN. A completed checkout can be registered as an internal order only once.

## Tests

Run `dotnet test Tests/back.Tests.csproj`. Tests use SQLite in memory and mocked HTTP responses; they do not contact PayPal or perform charges.