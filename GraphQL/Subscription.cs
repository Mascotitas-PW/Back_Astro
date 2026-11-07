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

    // Nombres explícitos: el front se suscribe a onStockActualizado, etc.
    [Subscribe, Topic(TopicStock), GraphQLName("onStockActualizado")]
    public StockActualizado OnStockActualizado([EventMessage] StockActualizado e) => e;

    [Subscribe, Topic(TopicProducto), GraphQLName("onProductoCambiado")]
    public ProductoCambiado OnProductoCambiado([EventMessage] ProductoCambiado e) => e;

    [Subscribe, Topic(TopicPedido), GraphQLName("onPedidoCreado")]
    public PedidoCreado OnPedidoCreado([EventMessage] PedidoCreado e) => e;
}
