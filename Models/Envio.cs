namespace back.Models;

public static class Envio
{
    // Envío gratis arriba de $500; de lo contrario $99.
    // Misma regla que usa el front en Checkout.jsx.
    public static decimal Calcular(decimal subtotal) =>
        subtotal > 500m ? 0m : 99m;
}
