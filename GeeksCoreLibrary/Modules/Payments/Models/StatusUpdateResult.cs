namespace GeeksCoreLibrary.Modules.Payments.Models
{
    public class StatusUpdateResult
    {
        /// <summary>
        /// Gets or sets whether the payment was successful.
        /// </summary>
        public bool Successful { get; set; }

        /// <summary>
        /// Gets or sets the status text or number that the PSP gave us.
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Gets or sets the status code that the PSP gave us.
        /// </summary>
        public int StatusCode { get; set; }
        
        /// <summary>
        /// Gets or sets the paid amount in this status update.
        /// </summary>
        public decimal PaidAmount { get; set; }
        
        /// <summary>
        /// Gets or sets the reference of the PSP for the transaction
        /// </summary>
        public string PspTransactionId { get; set; }
    }
}