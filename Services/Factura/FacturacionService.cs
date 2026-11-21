using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using back.Models;
using Microsoft.EntityFrameworkCore;

namespace back.Services.Factura;

// ── CATÁLOGOS SAT ──────────────────────────────────────────────────────────
public static class CatalogosSat
{
    public static readonly Dictionary<string, string> UsosCfdi = new()
    {
        ["G01"] = "G01 – Adquisición de mercancías",
        ["G03"] = "G03 – Gastos en general",
        ["I01"] = "I01 – Construcciones",
        ["D01"] = "D01 – Honorarios médicos, dentales y gastos hospitalarios",
        ["S01"] = "S01 – Sin efectos fiscales",
        ["CP01"] = "CP01 – Pagos",
    };

    public static readonly Dictionary<string, string> RegimenesFiscales = new()
    {
        ["601"] = "601 – General de Ley Personas Morales",
        ["603"] = "603 – Personas Morales con Fines no Lucrativos",
        ["605"] = "605 – Sueldos y Salarios e Ingresos Asimilados a Salarios",
        ["606"] = "606 – Arrendamiento",
        ["608"] = "608 – Demás ingresos",
        ["612"] = "612 – Personas Físicas con Actividades Empresariales y Profesionales",
        ["616"] = "616 – Sin obligaciones fiscales",
        ["621"] = "621 – Incorporación Fiscal",
        ["626"] = "626 – Régimen Simplificado de Confianza",
    };

    public static string FormaPago(string? metodo) => metodo switch
    {
        "Tarjeta" or "PayPal" => "04",
        "Efectivo" => "01",
        "Transferencia" => "03",
        _ => "99"
    };

    public static string ImporteEnLetra(decimal monto, string moneda = "MXN")
    {
        var enteros = (long)Math.Floor(monto);
        var centavos = (int)Math.Round((monto - enteros) * 100m, MidpointRounding.AwayFromZero);
        return $"{NumerosALetras(enteros)} {centavos:D2}/100 {moneda}";
    }

    private static string NumerosALetras(long n)
    {
        if (n == 0) return "CERO";
        if (n < 0) return "MENOS " + NumerosALetras(-n);
        string[] u = ["", "UN", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE",
            "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISEIS", "DIECISIETE",
            "DIECIOCHO", "DIECINUEVE", "VEINTE", "VEINTIUN", "VEINTIDOS", "VEINTITRES",
            "VEINTICUATRO", "VEINTICINCO", "VEINTISEIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"];
        string[] d = ["", "DIEZ", "VEINTE", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA"];
        string[] c = ["", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS",
            "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS"];
        if (n < 30) return u[n];
        if (n < 100) return d[n / 10] + (n % 10 != 0 ? " Y " + u[n % 10] : "");
        if (n == 100) return "CIEN";
        if (n < 1000) return c[n / 100] + (n % 100 != 0 ? " " + NumerosALetras(n % 100) : "");
        if (n < 2000) return "MIL" + (n % 1000 != 0 ? " " + NumerosALetras(n % 1000) : "");
        if (n < 1_000_000) return NumerosALetras(n / 1000) + " MIL" + (n % 1000 != 0 ? " " + NumerosALetras(n % 1000) : "");
        return NumerosALetras(n / 1_000_000) + " MILLON" + (n / 1_000_000 != 1 ? "ES" : "") +
               (n % 1_000_000 != 0 ? " " + NumerosALetras(n % 1_000_000) : "");
    }
}

// ── DATOS PARA GENERAR XML Y HTML ──────────────────────────────────────────
public sealed record ConceptoDoc(
    string ClaveProdServ, string ClaveUnidad, string UnidadNombre,
    string Descripcion, int Cantidad, decimal PrecioUnitario);

public sealed record DatosDoc(
    string Serie, int Folio, DateTime FechaEmision,
    string FormaPago, string MetodoPago,
    string RfcEmisor, string NombreEmisor, string RegimenEmisor, string CpEmisor,
    string RfcReceptor, string NombreReceptor, string RegimenReceptor,
    string CpReceptor, string? DomicilioReceptor, string UsoCfdi,
    decimal Subtotal, decimal Iva, decimal Total,
    List<ConceptoDoc> Conceptos);

// ── GENERADOR XML CFDI 4.0 ─────────────────────────────────────────────────
public static class XmlCfdi40
{
    public static string Generar(DatosDoc d)
    {
        string M(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        var conceptos = new StringBuilder();
        foreach (var c in d.Conceptos)
        {
            var imp = decimal.Round(c.PrecioUnitario * c.Cantidad, 2);
            var iva = decimal.Round(imp * 0.16m, 2);
            conceptos.AppendLine($"""
        <cfdi:Concepto ClaveProdServ="{c.ClaveProdServ}" Cantidad="{c.Cantidad}"
                       ClaveUnidad="{c.ClaveUnidad}" Unidad="{EX(c.UnidadNombre)}"
                       Descripcion="{EX(c.Descripcion)}"
                       ValorUnitario="{M(c.PrecioUnitario)}" Importe="{M(imp)}" ObjetoImp="02">
          <cfdi:Impuestos>
            <cfdi:Traslados>
              <cfdi:Traslado Base="{M(imp)}" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.160000" Importe="{M(iva)}"/>
            </cfdi:Traslados>
          </cfdi:Impuestos>
        </cfdi:Concepto>
        """);
        }

        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<cfdi:Comprobante
  xmlns:cfdi="http://www.sat.gob.mx/cfd/4"
  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
  xsi:schemaLocation="http://www.sat.gob.mx/cfd/4 http://www.sat.gob.mx/sitio_internet/cfd/4/cfdv40.xsd"
  Version="4.0" Serie="{EX(d.Serie)}" Folio="{d.Folio}"
  Fecha="{d.FechaEmision:yyyy-MM-ddTHH:mm:ss}"
  Sello="PENDIENTE_TIMBRADO" FormaPago="{d.FormaPago}"
  NoCertificado="PENDIENTE_TIMBRADO" Certificado="PENDIENTE_TIMBRADO"
  SubTotal="{M(d.Subtotal)}" Moneda="MXN" Total="{M(d.Total)}"
  TipoDeComprobante="I" MetodoPago="{d.MetodoPago}"
  LugarExpedicion="{d.CpEmisor}" Exportacion="01">

  <cfdi:Emisor Rfc="{d.RfcEmisor}" Nombre="{EX(d.NombreEmisor)}" RegimenFiscal="{d.RegimenEmisor}"/>

  <cfdi:Receptor Rfc="{d.RfcReceptor}" Nombre="{EX(d.NombreReceptor)}"
                 DomicilioFiscalReceptor="{d.CpReceptor}"
                 RegimenFiscalReceptor="{d.RegimenReceptor}" UsoCFDI="{d.UsoCfdi}"/>

  <cfdi:Conceptos>
{conceptos}  </cfdi:Conceptos>

  <cfdi:Impuestos TotalImpuestosTrasladados="{M(d.Iva)}">
    <cfdi:Traslados>
      <cfdi:Traslado Base="{M(d.Subtotal)}" Impuesto="002" TipoFactor="Tasa" TasaOCuota="0.160000" Importe="{M(d.Iva)}"/>
    </cfdi:Traslados>
  </cfdi:Impuestos>

</cfdi:Comprobante>
""";
    }

    private static string EX(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}

// ── GENERADOR HTML (representación impresa para el cliente) ────────────────
public static class HtmlCfdi40
{
    private static string M(decimal v) => v.ToString("N2", new CultureInfo("es-MX"));
    private static string EH(string? s) =>
        (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string Generar(DatosDoc d, string? uuid)
    {
        var filas = new StringBuilder();
        foreach (var c in d.Conceptos)
        {
            var imp = decimal.Round(c.PrecioUnitario * c.Cantidad, 2);
            var iva = decimal.Round(imp * 0.16m, 2);
            filas.Append($"""
            <tr>
              <td>{c.ClaveProdServ}</td>
              <td>{EH(c.Descripcion)}</td>
              <td class="r">{c.Cantidad}</td>
              <td>02</td>
              <td>{c.ClaveUnidad} – {EH(c.UnidadNombre)}</td>
              <td class="r">${M(c.PrecioUnitario)}</td>
              <td class="r">${M(imp)}</td>
            </tr>
            <tr class="sr"><td colspan="6" class="nota-iva">IVA 16%, Base: ${M(imp)}</td><td class="r nota-iva">${M(iva)}</td></tr>
""");
        }

        var uuidLinea = uuid is not null
            ? $"<b>Folio Fiscal (UUID)</b><span>{EH(uuid)}</span>"
            : "<b>Folio Fiscal</b><span><i>Documento en modo de pruebas</i></span>";

        var fpNombre = d.FormaPago switch
        {
            "01" => "Efectivo",
            "03" => "Transferencia electrónica de fondos",
            "04" => "Tarjeta de crédito/débito",
            _ => "Por definir"
        };

        // Nota: Se usa $$""" para que las llaves simples { } del CSS no causen error de compilación
        return $$"""
<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="utf-8">
  <title>Factura {{EH(d.Serie)}}-{{d.Folio}}</title>
  <style>
- { box-sizing: border-box; margin: 0; padding: 0; }
    body { font-family: 'Helvetica Neue', Arial, sans-serif; font-size: 12px; color: #111; padding: 32px; max-width: 920px; margin: 0 auto; }
    h1 { font-size: 26px; margin-bottom: 16px; }
    .r { text-align: right; }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; margin-bottom: 14px; }
    .box { border: 1px solid #ddd; border-radius: 4px; padding: 12px; }
    .box h2 { font-size: 10px; text-transform: uppercase; color: #555; border-bottom: 2px solid #1a4d8f; padding-bottom: 4px; margin-bottom: 8px; }
    .kv { display: grid; grid-template-columns: 150px 1fr; gap: 3px 8px; line-height: 1.6; }
    .kv b { font-weight: 600; }
    table { width: 100%; border-collapse: collapse; margin: 14px 0; }
    th { background: #1a4d8f; color: #fff; padding: 8px; text-align: left; font-size: 11px; }
    td { padding: 7px 8px; border-bottom: 1px solid #eee; vertical-align: top; }
    .sr td { border-bottom: none; padding-top: 2px; }
    .nota-iva { color: #666; font-size: .85em; }
    .totales { margin-left: auto; width: 280px; border-collapse: collapse; }
    .totales td { padding: 4px 8px; }
    .totales .total-row td { font-size: 14px; font-weight: 700; border-top: 2px solid #1a4d8f; padding-top: 6px; }
    .folio-box { border: 2px solid #1a4d8f; border-radius: 4px; padding: 8px 20px; text-align: center; float: right; }
    .folio-box span { display: block; font-size: 9px; color: #555; }
    .folio-box strong { font-size: 22px; letter-spacing: 1px; }
    .nota-pie { text-align: center; margin-top: 18px; color: #1a4d8f; font-size: 11px; border-top: 1px solid #ddd; padding-top: 10px; }
    @media print { body { padding: 16px; } }
  </style>
</head>
<body>

<div>
  <div class="folio-box"><span>Folio</span><strong>{{EH(d.Serie)}}{{d.Folio}}</strong></div>
  <div style="line-height:1.5">
    <div style="font-weight:700;font-size:13px">{{EH(d.NombreEmisor)}}</div>
    <div style="color:#555">VENTA DE ARTÍCULOS PARA MASCOTAS</div>
  </div>
  <h1 style="margin-top:12px">Factura</h1>
  <div style="clear:both"></div>
</div>

<div class="grid">
  <div class="box">
    <h2>Emisor</h2>
    <div style="font-weight:700;font-size:13px;margin-bottom:4px">{{EH(d.NombreEmisor)}}</div>
    <div class="kv">
      <b>RFC</b><span>{{d.RfcEmisor}}</span>
      <b>Régimen Fiscal</b><span>{{EH(CatalogosSat.RegimenesFiscales.GetValueOrDefault(d.RegimenEmisor, d.RegimenEmisor))}}</span>
      <b>Lugar emisión</b><span>C.P. {{d.CpEmisor}}</span>
    </div>
  </div>
  <div class="box">
    <div class="kv">
      {{uuidLinea}}
      <b>Tipo de CFDI</b><span>I (Ingreso)</span>
      <b>Versión CFDI</b><span>4.0</span>
      <b>Fecha emisión</b><span>{{d.FechaEmision:yyyy-MM-dd HH:mm:ss}}</span>
      <b>Moneda</b><span>MXN</span>
    </div>
  </div>
</div>

<div class="box" style="margin-bottom:14px">
  <h2>Receptor</h2>
  <div class="kv">
    <b>Razón Social</b><span>{{EH(d.NombreReceptor)}}</span>
    <b>RFC</b><span>{{d.RfcReceptor}}</span>
    <b>Régimen Fiscal</b><span>{{EH(CatalogosSat.RegimenesFiscales.GetValueOrDefault(d.RegimenReceptor, d.RegimenReceptor))}}</span>
    {{(d.DomicilioReceptor is not null
        ? $"<b>Domicilio Fiscal</b><span>{EH(d.DomicilioReceptor)}, C.P. {d.CpReceptor}</span>"
        : $"<b>C.P. Fiscal</b><span>{d.CpReceptor}</span>")}}
    <b>Uso del CFDI</b><span>{{EH(CatalogosSat.UsosCfdi.GetValueOrDefault(d.UsoCfdi, d.UsoCfdi))}}</span>
  </div>
</div>

<table>
  <thead>
    <tr>
      <th>Clave ProdServ</th><th>Descripción</th>
      <th class="r">Cant.</th><th>Obj. Imp.</th>
      <th>Unidad</th><th class="r">Precio Unitario</th><th class="r">Importe</th>
    </tr>
  </thead>
  <tbody>
    {{filas}}
  </tbody>
</table>

<div style="display:flex;justify-content:space-between;align-items:flex-end;flex-wrap:wrap;gap:16px">
  <div>
    <div class="kv" style="max-width:360px">
      <b>Forma de pago</b><span>{{d.FormaPago}} – {{fpNombre}}</span>
      <b>Método de pago</b><span>{{d.MetodoPago}} – Pago en una sola exhibición</span>
      <b>Moneda</b><span>MXN</span>
    </div>
    <p style="margin-top:8px;font-size:11px"><b>Importe en letra</b><br>{{CatalogosSat.ImporteEnLetra(d.Total)}}</p>
  </div>
  <table class="totales">
    <tr><td>Subtotal</td><td class="r">${{M(d.Subtotal)}}</td></tr>
    <tr><td>IVA 16%</td><td class="r">${{M(d.Iva)}}</td></tr>
    <tr class="total-row"><td>Total</td><td class="r">${{M(d.Total)}}</td></tr>
  </table>
</div>

<p class="nota-pie">Este documento es una representación impresa de un CFDI</p>

</body>
</html>
""";
    }
}

// ── SERVICIO PRINCIPAL ──────────────────────────────────────────────────────
public sealed class FacturacionService(
    AppDbContext context,
    IConfiguration cfg,
    HttpClient http,
    ILogger<FacturacionService> log)
{
    private bool ModoProduccion => cfg["Facturama:Produccion"] == "true";
    private string UrlBase => ModoProduccion
        ? "https://api.facturama.mx"
        : "https://apisandbox.facturama.mx";

    private string RfcEmisor => cfg["Factura:Emisor:Rfc"] ?? "XAXX010101000";
    private string NombreEmisor => cfg["Factura:Emisor:Nombre"] ?? "Mascotitas";
    private string RegimenEmisor => cfg["Factura:Emisor:Regimen"] ?? "612";
    private string CpEmisor => cfg["Factura:Emisor:CodigoPostal"] ?? "44100";

    // ── EMITIR ──────────────────────────────────────────────────────────────
    public async Task<Models.Factura> EmitirAsync(
        int pedidoId, int usuarioId,
        string rfcReceptor, string razonSocial, string regimenReceptor,
        string cpReceptor, string? domicilioReceptor, string usoCfdi,
        CancellationToken ct)
    {
        var pedido = await context.Pedidos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pedidoId, ct)
            ?? throw new FacturacionException("El pedido no existe.");

        // Se usa pedido.Status ya que EnvioEstado no existe en el modelo Pedido
        if (!string.Equals(pedido.Status, "Entregado", StringComparison.OrdinalIgnoreCase) &&
            pedido.Status?.Contains("Entregado", StringComparison.OrdinalIgnoreCase) != true)
        {
            throw new FacturacionException("El pedido debe estar entregado antes de emitir la factura.");
        }

        if (await context.Facturas.AnyAsync(
            f => f.PedidoId == pedidoId && f.UsuarioId == usuarioId &&
                 f.TipoComprobante == "I" && f.Estado != "Cancelada", ct))
            throw new FacturacionException("Ya emitiste una factura para este pedido.");

        rfcReceptor = rfcReceptor.Trim().ToUpperInvariant();
        razonSocial = razonSocial.Trim().ToUpperInvariant();
        cpReceptor = cpReceptor.Trim();

        if (rfcReceptor.Length is < 12 or > 13)
            throw new FacturacionException("RFC no válido (debe tener 12 o 13 caracteres).");
        if (razonSocial.Length < 3)
            throw new FacturacionException("Escribe la razón social o nombre completo.");
        if (cpReceptor.Length != 5 || !cpReceptor.All(char.IsDigit))
            throw new FacturacionException("El código postal fiscal debe tener 5 dígitos.");

        var detalles = await context.DetallePedidos.AsNoTracking()
            .Where(d => d.PedidoId == pedidoId).ToListAsync(ct);
        if (detalles.Count == 0)
            throw new FacturacionException("El pedido no tiene productos registrados.");

        var ultimoFolio = await context.Facturas
            .Where(f => f.Serie == "A")
            .MaxAsync(f => (int?)f.Folio, ct) ?? 0;

        var ids = detalles.Select(d => d.ProductoId).ToList();
        var prods = await context.Productos.AsNoTracking()
            .Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        // Clave SAT 65210301 = Artículos y accesorios para mascotas
        var conceptos = detalles.Select(d =>
        {
            var nombre = prods.TryGetValue(d.ProductoId, out var p) ? p.Nombre : $"Producto {d.ProductoId}";
            return new ConceptoDoc("65210301", "H87", "Pieza", nombre, d.Cantidad, (decimal)d.PrecioUnitario);
        }).ToList();

        var subtotal = conceptos.Sum(c => decimal.Round(c.PrecioUnitario * c.Cantidad, 2));
        var iva = conceptos.Sum(c => decimal.Round(decimal.Round(c.PrecioUnitario * c.Cantidad, 2) * 0.16m, 2));
        var total = subtotal + iva;

        var fp = pedido.Status?.Contains("PayPal", StringComparison.OrdinalIgnoreCase) == true ? "PayPal"
               : pedido.Status?.Contains("Mercado", StringComparison.OrdinalIgnoreCase) == true ? "Tarjeta"
               : "Transferencia";
        var formaPago = CatalogosSat.FormaPago(fp);

        var fechaMexico = ObtenerHoraMexico();

        var datos = new DatosDoc(
            "A", ultimoFolio + 1, fechaMexico,
            formaPago, "PUE",
            RfcEmisor, NombreEmisor, RegimenEmisor, CpEmisor,
            rfcReceptor, razonSocial, regimenReceptor, cpReceptor,
            domicilioReceptor, usoCfdi,
            subtotal, iva, total, conceptos);

        var xml = XmlCfdi40.Generar(datos);
        var html = HtmlCfdi40.Generar(datos, null);

        var factura = new Models.Factura
        {
            PedidoId = pedidoId, UsuarioId = usuarioId,
            TipoComprobante = "I", Serie = "A", Folio = ultimoFolio + 1,
            RfcReceptor = rfcReceptor, RazonSocialReceptor = razonSocial,
            RegimenFiscalReceptor = regimenReceptor,
            CodigoPostalReceptor = cpReceptor,
            DomicilioFiscalReceptor = domicilioReceptor,
            UsoCfdi = usoCfdi,
            Subtotal = subtotal, Iva = iva, Total = total,
            FormaPago = formaPago, MetodoPago = "PUE",
            FechaEmision = datos.FechaEmision,
            Xml = Encoding.UTF8.GetBytes(xml),
            Pdf = Encoding.UTF8.GetBytes(html),
            Estado = "Borrador"
        };

        var user = cfg["Facturama:Usuario"];
        var pass = cfg["Facturama:Password"];
        if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
        {
            try { await TimbrarAsync(factura, datos, user, pass, ct); }
            catch (FacturacionException) { throw; }
            catch (Exception ex)
            {
                log.LogError(ex, "Error al timbrar con Facturama el pedido {Id}", pedidoId);
                factura.Estado = "Borrador";
                factura.MotivoError = ex.Message;
            }
        }

        context.Facturas.Add(factura);
        await context.SaveChangesAsync(ct);
        return factura;
    }

    // ── TIMBRAR CON FACTURAMA ────────────────────────────────────────────────
    private async Task TimbrarAsync(
        Models.Factura factura, DatosDoc d, string user, string pass, CancellationToken ct)
    {
        var authHeader = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));

        var body = new
        {
            Serie = d.Serie,
            Folio = d.Folio.ToString(),
            Currency = "MXN",
            Date = d.FechaEmision.ToString("yyyy-MM-ddTHH:mm:ss"),
            PaymentForm = d.FormaPago,
            PaymentMethod = d.MetodoPago,
            Issuer = new { FiscalRegime = d.RegimenEmisor, Rfc = d.RfcEmisor, TaxName = d.NombreEmisor },
            Receiver = new
            {
                Rfc = d.RfcReceptor, Name = d.NombreReceptor,
                CfdiUse = d.UsoCfdi, FiscalRegime = d.RegimenReceptor,
                TaxZipCode = d.CpReceptor
            },
            Items = d.Conceptos.Select(c =>
            {
                var sub = decimal.Round(c.PrecioUnitario * c.Cantidad, 2);
                var tax = decimal.Round(sub * 0.16m, 2);
                return new
                {
                    ProductCode = c.ClaveProdServ,
                    UnitCode = c.ClaveUnidad,
                    Unit = c.UnidadNombre,
                    Description = c.Descripcion,
                    Quantity = (double)c.Cantidad,
                    UnitPrice = (double)c.PrecioUnitario,
                    Subtotal = (double)sub,
                    Taxes = new[] { new
                    {
                        Total = (double)tax,
                        Name = "IVA",
                        Rate = 0.16,
                        IsRetention = false,
                        IsFederal = true
                    }},
                    Total = (double)(sub + tax)
                };
            }).ToArray()
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{UrlBase}/2/cfdis")
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization = authHeader;

        using var resp = await http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new FacturacionException("Facturama rechazó el CFDI: " + json);

        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.GetProperty("Id").GetString()
            ?? throw new FacturacionException("Facturama no devolvió el ID del CFDI.");

        factura.FacturamaId = id;
        factura.Estado = "Timbrada";

        var xmlBytes = await DescargarArchivoFacturamaAsync($"{UrlBase}/cfdi/xml/issued/{id}", authHeader, ct);
        if (xmlBytes is not null) factura.Xml = xmlBytes;

        var pdfBytes = await DescargarArchivoFacturamaAsync($"{UrlBase}/cfdi/pdf/issued/{id}", authHeader, ct);
        if (pdfBytes is not null) factura.Pdf = pdfBytes;

        if (factura.Xml is not null)
        {
            var xmlStr = Encoding.UTF8.GetString(factura.Xml);
            var m = System.Text.RegularExpressions.Regex.Match(xmlStr, @"UUID=""([^""]+)""");
            if (m.Success)
            {
                factura.Uuid = m.Groups[1].Value;
                if (pdfBytes is null)
                    factura.Pdf = Encoding.UTF8.GetBytes(HtmlCfdi40.Generar(d, factura.Uuid));
            }
        }
    }

    private async Task<byte[]?> DescargarArchivoFacturamaAsync(
        string url, AuthenticationHeaderValue auth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = auth;

        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var raw = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("Content", out var contentProp) &&
            contentProp.GetString() is { Length: > 0 } base64)
        {
            return Convert.FromBase64String(base64);
        }
        return null;
    }

    private static DateTime ObtenerHoraMexico()
    {
        DateTime local;
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
            local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            local = DateTime.UtcNow.AddHours(-6);
        }
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }
}

public sealed class FacturacionException(string message) : Exception(message);