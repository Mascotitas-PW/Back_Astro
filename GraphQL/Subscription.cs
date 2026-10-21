using back.Models;
using HotChocolate;
using HotChocolate.Types;

namespace back.GraphQL;

public class Subscription
{
    [Subscribe]
    [Topic(ProductoMutation.TopicProductoActualizado)]
    public Producto OnProductoActualizado([EventMessage] Producto producto) => producto;
}
