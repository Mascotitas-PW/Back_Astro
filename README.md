# Mascotitas — Backend

API de la tienda de mascotas **Mascotitas**. Expone una API **GraphQL** (HotChocolate) y algunos endpoints REST para pagos e imágenes, sobre **ASP.NET Core** con **PostgreSQL**.

## Tecnologías

| Área | Tecnología |
|---|---|
| Plataforma | .NET 10 / ASP.NET Core (minimal APIs) |
| API | HotChocolate GraphQL (queries, mutations y subscriptions por WebSocket) |
| Base de datos | PostgreSQL con Entity Framework Core (Npgsql) |
| Autenticación | JWT Bearer, contraseñas con BCrypt, inicio de sesión con Google |
| Pagos | Mercado Pago (tarjeta) y PayPal; efectivo y transferencia sin pago en línea |
| Imágenes | Subida local validada por firma de archivo (JPG, PNG, WEBP, máx. 5 MB) |
| CI | GitHub Actions (`Back CI`) |

## Funcionalidades actuales

- **Usuarios:** registro, login con correo/contraseña y login con Google. Todo usuario nuevo se crea con rol `Cliente`.
- **Catálogo:** consulta de categorías y productos (con filtrado y ordenamiento).
- **Administración (rol `Admin`):** crear, actualizar y eliminar productos; subir imágenes; consultar usuarios, todos los pedidos y estadísticas.
- **Pedidos:** creación con pago por tarjeta (Mercado Pago), con PayPal, o sin pago en línea (`Efectivo` / `Transferencia`, queda como `Pendiente de pago`).
- **Reglas de negocio en el servidor:** el total siempre lo calcula el backend (productos + envío), se valida stock y datos de entrega, y el stock se descuenta en una transacción.
<<<<<<< HEAD
- **Envío:** gratis en subtotales mayores a $500 MXN; de lo contrario $99 MXN.
=======
- **Envío:** cotización de paqueterías Skydropx en Sandbox; el usuario elige un servicio y el backend valida y guarda la tarifa junto con el pedido.
- **Paquetes:** el peso se calcula con el peso neto de cada producto más la tara configurada de la caja; las dimensiones de la caja se configuran en el backend.
>>>>>>> 95a2c0e (envios)
- **Tiempo real:** subscriptions para cambios de stock, de productos y pedidos nuevos (`InMemorySubscriptions`).
- **Estadísticas:** ventas totales, pedidos, ticket promedio, ventas de los últimos 30 días, top 5 de productos y ventas por categoría.

## Estructura

```text
back/
├── Program.cs                  # Configuración, DI, auth, CORS, GraphQL y endpoints
├── AppDbContext.cs             # DbContext y datos semilla
├── GraphQL/
│   ├── Query.cs
│   ├── Mutation.cs             # registrar, login, loginGoogle
│   ├── ProductMutation.cs      # CRUD de productos (Admin)
│   ├── PedidoMutation.cs       # crearPedido, crearPedidoSinPago
│   ├── PedidoAuth.cs
│   └── Subscription.cs
├── Models/                     # User, Categoria, Producto, Pedido, DetallePedido, EstadisticasDto
├── Payments/
│   ├── MercadoPagoPaymentService.cs
│   ├── ProcessOrderEndpoint.cs / ProcessOrderRequest.cs
│   ├── Envio.cs
│   └── PayPal/                 # PayPalEndpoint, PayPalPaymentService, PayPalModels
├── Services/
│   ├── Auth/                   # AuthService, JwtService, AuthHelper, AuthPayload
│   ├── Pedidos/PedidoService.cs  # Único lugar donde se crea un pedido
│   └── ImageService.cs
└── .github/workflows/deploy.yml
```

## Configuración

Las claves se leen de `appsettings`, *user-secrets* o variables de entorno (en variables de entorno, los niveles se separan con doble guion bajo, por ejemplo `Jwt__Key`).

| Clave | Obligatoria | Descripción |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | Sí | Cadena de conexión a PostgreSQL |
| `Jwt:Key` | Sí | Llave de firma de los tokens (la app no arranca sin ella) |
| `Jwt:Issuer` / `Jwt:Audience` | No | Por defecto `mascotitas` |
| `Jwt:ExpireHours` | No | Duración del token, por defecto 8 horas |
| `Google:ClientId` | Para login con Google | Client ID de OAuth de Google |
| `MP_TOKEN` | Para Mercado Pago | Access token de Mercado Pago |
| `MERCADOPAGO_CURRENCY` | No | Moneda, por defecto `MXN` |
| `PAYPAL_CLIENT_ID` / `PAYPAL_CLIENT_SECRET` | Para PayPal | Credenciales de PayPal |
| `PayPal:BaseUrl` | No | Por defecto `https://api-m.sandbox.paypal.com` (sandbox) |
| `PayPal:Currency` | No | Por defecto `MXN` |
<<<<<<< HEAD
=======
| `Skydropx:BaseUrl` | Para cotizar | Host Sandbox indicado para tu cuenta Skydropx; no uses el host de producción |
| `Skydropx:ClientId` / `Skydropx:ClientSecret` | Para cotizar | Credenciales API de la aplicación Sandbox |
| `Skydropx:Origin:PostalCode` | Para cotizar | Código postal de la bodega de salida |
| `Skydropx:Origin:State` / `City` / `Neighborhood` | Para cotizar | Estado, ciudad y colonia de la bodega |
| `Skydropx:Parcel:LengthCm` / `WidthCm` / `HeightCm` | Para cotizar | Dimensiones exteriores de la caja estándar, en centímetros |
| `Skydropx:Parcel:TareWeightKg` | Para cotizar | Peso vacío de la caja, en kilogramos |
>>>>>>> 95a2c0e (envios)

Ejemplo con *user-secrets*:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=mascotitas;Username=postgres;Password=tu_password"
dotnet user-secrets set "Jwt:Key" "una-llave-larga-y-secreta-de-al-menos-32-caracteres"
<<<<<<< HEAD
```

=======
dotnet user-secrets set "Skydropx:BaseUrl" "<host Sandbox indicado por Skydropx>"
dotnet user-secrets set "Skydropx:ClientId" "<client id de Sandbox>"
dotnet user-secrets set "Skydropx:ClientSecret" "<client secret de Sandbox>"
dotnet user-secrets set "Skydropx:Origin:PostalCode" "<cp de la bodega>"
dotnet user-secrets set "Skydropx:Origin:State" "<estado de la bodega>"
dotnet user-secrets set "Skydropx:Origin:City" "<ciudad de la bodega>"
dotnet user-secrets set "Skydropx:Origin:Neighborhood" "<colonia de la bodega>"
dotnet user-secrets set "Skydropx:Parcel:LengthCm" "<largo de caja en cm>"
dotnet user-secrets set "Skydropx:Parcel:WidthCm" "<ancho de caja en cm>"
dotnet user-secrets set "Skydropx:Parcel:HeightCm" "<alto de caja en cm>"
dotnet user-secrets set "Skydropx:Parcel:TareWeightKg" "<peso de caja vacía en kg>"
```

El portal público de Skydropx documenta `POST /api/v1/quotations`, `GET /api/v1/quotations/{id}` y el token OAuth, pero publica el host de producción en su OpenAPI. Confirma el host Sandbox exacto en la aplicación Sandbox de tu cuenta antes de configurar `Skydropx:BaseUrl`. No compartas aquí el client secret.

Para probar: actualiza cada producto desde Administración con su peso neto en kg; captura el estado, ciudad, colonia y código postal del destino; y revisa que la caja configurada pueda contener el pedido. La tarifa se calcula en MXN, vence en 24 horas y se persiste en el pedido. El servicio respeta el procesamiento asíncrono de cotizaciones y consulta hasta que Skydropx las completa.

>>>>>>> 95a2c0e (envios)
## Ejecución local

Requisitos: SDK de .NET 10 y una instancia de PostgreSQL.

```bash
dotnet restore back.csproj
dotnet run --project back.csproj
```

El front espera el backend en `http://localhost:5097/graphql` cuando se trabaja en local.

## Base de datos

- Tablas mapeadas: `Usuarios`, `Categorias`, `Productos`, `Pedido` y `DetallePedido`.
- Datos semilla: 3 categorías (Alimentos, Juguetes, Accesorios) y 6 productos de ejemplo.
- Para crear un administrador hay que cambiar el campo `Rol` del usuario a `Admin` directamente en la base de datos; el registro público siempre crea clientes.

## Endpoints

### GraphQL — `/graphql`

| Tipo | Operación | Acceso |
|---|---|---|
| Query | `categorias`, `productos` | Público |
| Query | `pedidosPorUsuario`, `pedidoPorId`, `detallePedido` | Autenticado (Admin o dueño del pedido) |
| Query | `users`, `pedidos`, `estadisticas` | Admin |
| Mutation | `registrar`, `login`, `loginGoogle` | Público |
| Mutation | `crearPedido`, `crearPedidoSinPago` | Autenticado (a nombre propio, o Admin) |
| Mutation | `crearProducto`, `actualizarProducto`, `eliminarProducto` | Admin |
| Subscription | `onStockActualizado`, `onProductoCambiado`, `onPedidoCreado` | Público |

### REST

| Método y ruta | Descripción | Acceso |
|---|---|---|
| `POST /process_order` | Cobra la tarjeta con Mercado Pago | Autenticado |
| `POST /api/orders` | Crea la orden de PayPal | Autenticado |
| `POST /api/orders/{orderId}/capture` | Captura el pago de PayPal y crea el pedido | Autenticado |
<<<<<<< HEAD
=======
| `POST /api/envios/cotizaciones` | Cotiza el paquete y guarda las tarifas devueltas por Skydropx | Autenticado |
>>>>>>> 95a2c0e (envios)
| `POST /api/imagenes` | Sube una imagen de producto | Admin |
| `GET /images/{archivo}` | Sirve las imágenes subidas | Público |
| `POST /webhook/mercadopago` | Recibe notificaciones (por ahora solo las registra en el log) | Público |

## Flujo de pago con tarjeta (Mercado Pago)

1. El front envía los datos de la tarjeta a `POST /process_order`; el backend calcula el total y cobra.
2. Con el pago aprobado, el front llama a la mutation `crearPedido` con el `paymentId`.
3. El backend vuelve a consultar el pago en Mercado Pago (estado, importe, moneda, usuario y productos) y **solo entonces** guarda el pedido y descuenta el stock.

## CI

El workflow `Back CI` (`.github/workflows/deploy.yml`) se ejecuta en cada push y pull request a `main`: restaura dependencias, compila en Release, publica y sube el artefacto `back-publish`. Las pruebas y la verificación de formato están comentadas hasta que exista un proyecto de pruebas.

## Notas

- CORS está abierto a cualquier origen (`AllowReact`).
- Las subscriptions usan memoria del proceso, por lo que solo funcionan con una instancia del servidor.
- Las imágenes se guardan en `uploads/images` dentro del directorio de la aplicación.
