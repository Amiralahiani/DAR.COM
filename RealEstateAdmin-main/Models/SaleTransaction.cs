using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RealEstateAdmin.Models
{
    public class SaleTransaction
    {
        public int Id { get; set; }

        [Required]
        public int BienImmobilierId { get; set; }

        [ForeignKey("BienImmobilierId")]
        public BienImmobilier? BienImmobilier { get; set; }

        [StringLength(450)]
        public string? BuyerId { get; set; }

        [ForeignKey("BuyerId")]
        public UserReference? Buyer { get; set; }

        [StringLength(450)]
        public string? SellerId { get; set; }

        [ForeignKey("SellerId")]
        public UserReference? Seller { get; set; }

        [Range(0, double.MaxValue)]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(50)]
        public string PaymentMethod { get; set; } = "Virement";

        [StringLength(50)]
        public string PaymentStatus { get; set; } = "En attente";

        [StringLength(50)]
        public string TransactionStatus { get; set; } = "Finalisée";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? PaidAt { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }
}
