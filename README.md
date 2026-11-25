# Mascotitas — Back-end

API de la tienda Mascotitas hecha con **ASP.NET Core**, **GraphQL (Hot Chocolate)** y **PostgreSQL** (Entity Framework Core). Maneja autenticación, catálogo, pedidos, pagos, cotización de envíos, domicilios, facturación CFDI 4.0 y eventos en tiempo real.

## Tecnologías

|Área|Herramienta|
|---|---|
|Framework|ASP.NET Core (minimal APIs + GraphQL)|
|GraphQL|Hot Chocolate: queries, mutations, **subscriptions** (WebSocket), filtrado y ordenamiento|
|Base de datos|PostgreSQL con EF Core (Npgsql)|
|Autenticación|JWT Bearer (HS256), contraseñas con **BCrypt**, inicio con **Google**|
|Pagos|Mercado Pago (tarjeta) y PayPal (Orders v2)|
|Envíos|Skydropx (cotización con tarifas)|
|Facturación|Facturama (timbrado CFDI 4.0)|
|Imágenes|Cloudinary (o disco local si no hay `CLOUDINARY_URL`)|
|Códigos postales|Zippopotam.us + Nominatim (ciudad)|
|Pruebas|xUnit + `WebApplicationFactory` + base en memoria|

## Estructura

```text
back/
├── Program.cs                 Servicios, middleware, endpoints REST y preparación de la BD
├── AppDbContext.cs            DbSets, nombres de tablas y datos semilla
├── GraphQL/
│   ├── Query.cs               catálogo, pedidos, estadísticas
│   ├── Mutation.cs            registrar, login, loginGoogle
│   ├── ProductMutation.cs     CRUD de productos (Admin)
│   ├── PedidoMutation.cs      crearPedido / crearPedidoSinPago
│   ├── DomicilioGraphQL.cs    domicilios y aceptación legal
│   ├── FacturaGraphQL.cs      datos fiscales y facturas
│   ├── PedidoAuth.cs          validaciones de propietario
│   └── Subscription.cs        stock, productos y pedidos en vivo
├── Models/                    Pedido, Producto, Factura, Domicilio, CotizacionEnvio…
├── Payments/
│   ├── MercadoPagoPaymentService.cs, ProcessOrderEndpoint.cs
│   ├── PayPal/                PayPalPaymentService, PayPalEndpoint, modelos
│   ├── SkydropxClient.cs, EnvioEndpoint.cs, CotizacionValidation.cs
│   └── MexicoPostalService.cs
├── Services/
│   ├── Auth/                  AuthService, JwtService, AuthHelper
│   ├── Pedidos/PedidoService.cs   ÚNICO lugar donde se crea un pedido
│   ├── Factura/FacturacionService.cs   XML/HTML CFDI + timbrado
│   └── CloudinaryImageService.cs / ImageService.cs
├── Http/ApiErrorHandling.cs   Manejo global de errores (500 genérico con CORS)
└── Tests/                     Pruebas de envíos, Skydropx, postal y Mercado Pago
```

## Instalación

Requisitos: **.NET SDK**, **PostgreSQL** accesible y, para los pagos/envíos, cuentas de prueba de cada proveedor.

```bash
cd back
dotnet restore
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key" "<mínimo 32 caracteres>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=mascotitas;Username=postgres;Password=<clave>"
dotnet run
```

GraphQL queda en `/graphql` (con la herramienta Banana Cake Pop en desarrollo). Al arrancar, el servidor crea las tablas y columnas faltantes (`CREATE/ALTER … IF NOT EXISTS`), por lo que no se requieren migraciones para empezar.

## Configuración

Se lee de *user-secrets*, `appsettings` o variables de entorno (en Render/Linux se usa `__` en lugar de `:`, por ejemplo `Jwt__Key`).

|Clave|Obligatoria|Descripción|
|---|---|---|
|`ConnectionStrings:DefaultConnection`|Sí|Cadena de PostgreSQL|
|`Jwt:Key`|Sí|Clave de firma (32+ caracteres)|
|`Jwt:Issuer` / `Jwt:Audience`|No|Por defecto `mascotitas`|
|`Jwt:ExpireHours`|No|Duración del token (por defecto 8)|
|`Google:ClientId`|Para Google|ID de cliente de Google|
|`MP_TOKEN`|Para tarjeta|Access token de Mercado Pago|
|`MERCADOPAGO_CURRENCY`|No|Por defecto `MXN`|
|`PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`|Para PayPal|Credenciales de la app|
|`PayPal:BaseUrl`, `PayPal:Currency`|No|Sandbox y `MXN` por defecto|
|`Skydropx:BaseUrl`, `Skydropx:ClientId`, `Skydropx:ClientSecret`|Para envíos|API de Skydropx|
|`Skydropx:Origin:PostalCode` `State` `City` `Neighborhood`|Para envíos|Dirección de origen|
|`Skydropx:Parcel:LengthCm` `WidthCm` `HeightCm`|Para envíos|Medidas de la caja (positivas)|
|`Skydropx:Parcel:TareWeightKg`|No|Peso de la caja vacía|
|`Facturama:Usuario`, `Facturama:Password`|Para timbrar|Sin ellas la factura queda como **Borrador**|
|`Facturama:Produccion`|No|`true` para producción; sandbox por defecto|
|`Factura:Emisor:Rfc` `Nombre` `Regimen` `CodigoPostal`|Para facturar|Datos del emisor|
|`CLOUDINARY_URL`|No|Si falta, las imágenes se guardan en `uploads/images`|

**CORS** permite `http://localhost:4321` y `https://mascotitas-pw.github.io` (se cambia en `Program.cs`, política `AllowReact`).

## API

### GraphQL (`/graphql`)

|Tipo|Operaciones|Acceso|
|---|---|---|
|Query|`categorias`, `productos`|Público|
|Query|`pedidos`, `users`, `estadisticas`|Admin|
|Query|`pedidosPorUsuario`, `pedidoPorId`, `detallePedido`|Admin o propietario|
|Query|`misDomicilios`, `terminosAceptados`, `misFacturas`, `misDatosFiscales`|Sesión iniciada|
|Query|`catalogoUsosCfdi`, `catalogoRegimenesFiscales`|Público|
|Mutation|`registrar`, `login`, `loginGoogle`|Público|
|Mutation|`crearProducto`, `actualizarProducto`, `eliminarProducto`|Admin|
|Mutation|`crearPedido` (tarjeta), `crearPedidoSinPago` (efectivo/transferencia)|Sesión (propietario o Admin)|
|Mutation|`guardarDomicilio`, `eliminarDomicilio`, `aceptarTerminos`|Sesión|
|Mutation|`guardarDatosFiscales`, `solicitarFactura`|Sesión|
|Subscription|`onStockActualizado`, `onProductoCambiado`, `onPedidoCreado`|Público|

El registro **siempre crea clientes**: el parámetro `rol` se ignora. Para crear un administrador se cambia el campo `Rol` a `Admin` directamente en la base de datos.

### REST

|Método y ruta|Acceso|Función|
|---|---|---|
|`POST /process_order`|Sesión|Cobra con Mercado Pago (monto calculado en el servidor)|
|`POST /api/orders`|Sesión|Crea la orden de PayPal|
|`POST /api/orders/{orderId}/capture`|Sesión|Captura el pago y crea el pedido|
|`POST /api/envios/cotizaciones`|Sesión|Cotiza el envío (vigencia 24 h)|
|`GET /api/codigos-postales/{cp}`|Público|Estado, ciudad y colonias|
|`POST /api/imagenes`|Admin|Sube una imagen (JPG/PNG/WEBP, máx. 5 MB, valida la firma del archivo)|
|`GET /api/facturas/{id}/xml` · `/pdf`|Admin o propietario|Descarga de la factura|
|`POST /webhook/mercadopago`|Público|Recibe notificaciones (solo las registra)|

## Reglas de negocio importantes

- **El total lo calcula el servidor** con `PedidoService.CalcularTotalAsync`: suma de productos + tarifa de envío elegida.
- **Verificación antes de guardar:** con Mercado Pago se consulta el pago (estado `approved`, importe, moneda, usuario y productos del `metadata`); con PayPal se valida y captura la orden. Si algo no coincide, **no se crea el pedido**.
- **Transacción:** descuento de stock, pedido y detalle se guardan juntos; si falla, se revierte.
- **Cotización de envío:** debe pertenecer al usuario, no estar vencida y coincidir con el domicilio (CP, estado, ciudad y colonia).
- **Factura:** solo para pedidos con estado que contenga "Entregado", una por pedido, IVA 16 %, clave SAT `65210301`.
- **Errores:** las excepciones de validación llegan al cliente como mensajes legibles; cualquier otra devuelve un 500 genérico sin detalles internos.

## Pruebas

```bash
dotnet test
```

Las pruebas de integración levantan la API real con base de datos **en memoria** y dobles de Skydropx y del servicio postal; **no usan credenciales ni tokens reales**.

## Despliegue

Pensado para **Render** (servicio web .NET) con PostgreSQL. Define las variables de entorno de la tabla de configuración, usa `Jwt__Key` (doble guion bajo) y verifica que el origen del front esté en la política CORS.