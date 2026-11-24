namespace back.Models;

public class DatosFiscalesCliente
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public string Alias { get; set; } = "Principal";
    public string Rfc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string RegimenFiscal { get; set; } = string.Empty;
    public string CodigoPostalFiscal { get; set; } = string.Empty;
    public string? DomicilioFiscal { get; set; }
    public bool Predeterminado { get; set; }
}

public class Factura
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int UsuarioId { get; set; }
    public int NumeroPedido { get; set; }
    public string TipoComprobante { get; set; } = "I";
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
    public string? Uuid { get; set; }
    public string? FacturamaId { get; set; }

    public byte[]? Xml { get; set; }
    public byte[]? Pdf { get; set; }

    public string Origen { get; set; } = "Cliente";
    public string Estado { get; set; } = "Borrador";
    public string? MotivoError { get; set; }
}