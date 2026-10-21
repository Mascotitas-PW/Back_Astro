using HotChocolate;
using HotChocolate.Types;

namespace back.GraphQL;

[GraphQLName("StockActualizado")]
public record StockActualizado(int ProductoId, int Stock);

public class Subscription
{
    public const string TopicStock = "StockActualizado";

    [Subscribe]
    [Topic(TopicStock)]
    public StockActualizado OnStockActualizado([EventMessage] StockActualizado evento) => evento;
}
