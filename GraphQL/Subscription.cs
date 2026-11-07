using HotChocolate;
using HotChocolate.Types;

namespace back.GraphQL;

[GraphQLName("StockActualizado")]
public record StockActualizado(int ProductoId, int Stock);

[GraphQLName("ProductoCambiado")]
public record ProductoCambiado(string Tipo, int ProductoId); // creado | actualizado | eliminado

[GraphQLName("PedidoCreado")]
public record PedidoCreado(int PedidoId, int UsuarioId);

public class Subscription
{
    public const string TopicStock = "StockActualizado";
    public const string TopicProducto = "ProductoCambiado";
    public const string TopicPedido = "PedidoCreado";

    [Subscribe, Topic(TopicStock)]
    public StockActualizado OnStockActualizado([EventMessage] StockActualizado e) => e;

    [Subscribe, Topic(TopicProducto)]
    public ProductoCambiado OnProductoCambiado([EventMessage] ProductoCambiado e) => e;

    [Subscribe, Topic(TopicPedido)]
    public PedidoCreado OnPedidoCreado([EventMessage] PedidoCreado e) => e;
}
