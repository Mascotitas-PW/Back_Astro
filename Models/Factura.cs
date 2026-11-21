namespace back.Models;

// Perfil fiscal del cliente (reutilizable para múltiples facturas)
public class DatosFiscalesCliente
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public string Alias { get; set; } = "Principal";
    public string Rfc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string RegimenFiscal { get; set; } = string.Empty;  // "601", "612", etc.
    public string CodigoPostalFiscal { get; set; } = string.Empty;
    public string? DomicilioFiscal { get; set; }
    public bool Predeterminado { get; set; }
}

// Factura CFDI 4.0 generada para un pedido.
// XML y PDF se guardan en la base de datos, nunca en Cloudinary.
public class Factura
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int UsuarioId { get; set; }
    public string TipoComprobante { get; set; } = "I";     // I = Ingreso
    public string Serie { get; set; } = "A";
    public int Folio { get; set; }

    // Datos del receptor
    public string RfcReceptor { get; set; } = string.Empty;
    public string RazonSocialReceptor { get; set; } = string.Empty;
    public string RegimenFiscalReceptor { get; set; } = string.Empty;
    public string CodigoPostalReceptor { get; set; } = string.Empty;
    public string? DomicilioFiscalReceptor { get; set; }
    public string UsoCfdi { get; set; } = "G03";

    // Totales
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }

    // Pago
    public string FormaPago { get; set; } = "03";
    public string MetodoPago { get; set; } = "PUE";
    public DateTime FechaEmision { get; set; }

    // Facturama
    public string? Uuid { get; set; }          // folio fiscal; null hasta que se timbra
    public string? FacturamaId { get; set; }

    // Archivos (nunca en disco: viven en la BDD para no perderse en deploys)
    public byte[]? Xml { get; set; }
    public byte[]? Pdf { get; set; }

    // Borrador | Timbrada | Cancelada
    public string Estado { get; set; } = "Borrador";
    public string? MotivoError { get; set; }
}
