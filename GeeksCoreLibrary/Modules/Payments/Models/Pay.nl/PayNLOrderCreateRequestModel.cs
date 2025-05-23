namespace GeeksCoreLibrary.Modules.Payments.PayNl.Models;

public class PayNLOrderCreateRequestModel
{
        public Amount Amount { get; set; }
        public string ServiceId { get; set; }
        public string Description { get; set; }
        public string Reference { get; set; }
        public string ReturnUrl { get; set; }
        public string ExchangeUrl { get; set; }
        public Integration Integration { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
}

public class Amount
{
    public int Value { get; set; }
}

public class Integration
{
    public bool Test { get; set; }
}

public class PaymentMethod
{
    public int Id { get; set; }
}