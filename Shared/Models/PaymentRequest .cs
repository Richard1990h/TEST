public class PaymentRequest
{
    public string PriceId { get; set; } = "";
    public string Mode { get; set; } = "payment";
    public string SuccessUrl { get; set; } = "";
    public string CancelUrl { get; set; } = "";
}
