using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RealEstateAdmin.Data;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public DashboardService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<DashboardData> BuildAsync(string? currentUserId, bool isAdmin)
        {
            var data = new DashboardData
            {
                IsAdmin = isAdmin
            };

            var biensQuery = _context.Biens.AsQueryable();
            if (!isAdmin && !string.IsNullOrWhiteSpace(currentUserId))
            {
                biensQuery = biensQuery.Where(b => b.UserId == currentUserId);
            }

            data.TotalBiens = await biensQuery.CountAsync();
            data.TotalUtilisateurs = isAdmin ? await _userManager.Users.CountAsync() : 0;
            data.TotalMessages = isAdmin ? await _context.Messages.CountAsync() : 0;

            var prixList = await biensQuery.Select(b => b.Prix).ToListAsync();
            data.BiensParPrix = BuildPriceBuckets(prixList);

            var zoneSource = await biensQuery
                .Select(b => new { b.Prix, b.Surface, b.Adresse })
                .ToListAsync();

            var zoneStats = zoneSource
                .GroupBy(b => ExtractZone(b.Adresse))
                .Select(g => new DashboardZoneStat
                {
                    Zone = g.Key,
                    Count = g.Count(),
                    AvgPrice = g.Average(x => x.Prix),
                    AvgPricePerM2 = g.Where(x => x.Surface.HasValue && x.Surface.Value > 0)
                        .Select(x => (double)(x.Prix / x.Surface!.Value))
                        .DefaultIfEmpty(0)
                        .Average()
                })
                .OrderByDescending(z => z.Count)
                .ToList();

            data.ZoneStats = zoneStats;
            data.TopZone = zoneStats.FirstOrDefault()?.Zone ?? "-";
            data.AvgPrice = prixList.Count > 0 ? $"{FormatMoney(prixList.Average())} DT" : "-";
            data.AvgPricePerM2 = zoneStats.Any(z => z.AvgPricePerM2 > 0)
                ? $"{FormatMoney((decimal)zoneStats.Where(z => z.AvgPricePerM2 > 0).Average(z => z.AvgPricePerM2))} DT/m²"
                : "-";
            data.ZoneCount = zoneStats.Count;

            if (isAdmin)
            {
                var sales = _context.Sales.AsQueryable();
                var totalTransactions = await sales.CountAsync();
                var paidTransactions = await sales.CountAsync(s => s.PaymentStatus == "Payé");
                var totalRevenue = await sales.SumAsync(s => (decimal?)s.Amount) ?? 0;
                var publishedBiens = await _context.Biens.CountAsync(b => b.PublicationStatus == "Publié");
                var soldBiens = await _context.Biens.CountAsync(b => b.StatutCommercial == "Vendu");

                var revenueByMethod = await sales
                    .GroupBy(s => s.PaymentMethod)
                    .Select(g => new DashboardRevenueByMethod
                    {
                        Method = g.Key,
                        Amount = g.Sum(x => x.Amount)
                    })
                    .OrderByDescending(x => x.Amount)
                    .ToListAsync();

                var conversionRate = publishedBiens > 0 ? Math.Round((double)soldBiens * 100d / publishedBiens, 2) : 0d;

                data.TotalTransactions = totalTransactions;
                data.PaidTransactions = paidTransactions;
                data.TotalRevenue = $"{FormatMoney(totalRevenue)} DT";
                data.ConversionRate = $"{conversionRate:N2}%";
                data.RevenueByMethod = revenueByMethod;
            }
            else
            {
                data.MySales = string.IsNullOrWhiteSpace(currentUserId) ? 0 : await _context.Sales.CountAsync(s => s.SellerId == currentUserId);
                data.MyPurchases = string.IsNullOrWhiteSpace(currentUserId) ? 0 : await _context.Sales.CountAsync(s => s.BuyerId == currentUserId);
                var myTransactionAmount = string.IsNullOrWhiteSpace(currentUserId)
                    ? 0
                    : await _context.Sales.Where(s => s.BuyerId == currentUserId).SumAsync(s => (decimal?)s.Amount) ?? 0;
                data.MyTransactionAmount = $"{FormatMoney(myTransactionAmount)} DT";
            }

            return data;
        }

        private static List<object> BuildPriceBuckets(List<decimal> prices)
        {
            var buckets = new List<object>();
            if (prices == null || prices.Count == 0)
            {
                return buckets;
            }

            prices.Sort();
            var min = prices.First();
            var max = prices.Last();
            if (min == max)
            {
                buckets.Add(new { Categorie = $"{FormatMoney(min)} DT", Count = prices.Count });
                return buckets;
            }

            var binCount = Math.Clamp((int)Math.Ceiling(Math.Sqrt(prices.Count)), 3, 6);
            var step = NiceStep(max - min, binCount);
            if (step <= 0)
            {
                step = 1;
            }

            var minEdge = Math.Floor(min / step) * step;
            var maxEdge = Math.Ceiling(max / step) * step;
            if (maxEdge == minEdge)
            {
                maxEdge = minEdge + step;
            }

            var binTotal = (int)Math.Ceiling((maxEdge - minEdge) / step);
            var counts = new int[binTotal];

            foreach (var price in prices)
            {
                var idx = (int)Math.Floor((price - minEdge) / step);
                if (idx < 0) idx = 0;
                if (idx >= binTotal) idx = binTotal - 1;
                counts[idx]++;
            }

            for (var i = 0; i < binTotal; i++)
            {
                var start = minEdge + (i * step);
                var end = start + step;
                var label = $"{FormatMoney(start)} - {FormatMoney(end)} DT";
                buckets.Add(new { Categorie = label, Count = counts[i] });
            }

            return buckets;
        }

        private static decimal NiceStep(decimal range, int bins)
        {
            if (range <= 0 || bins <= 0)
            {
                return 1;
            }

            var raw = (double)(range / bins);
            var exponent = Math.Floor(Math.Log10(raw));
            var power = Math.Pow(10, exponent);
            var fraction = raw / power;
            var niceFraction = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
            return (decimal)(niceFraction * power);
        }

        private static string FormatMoney(decimal value)
        {
            return value.ToString("N0");
        }

        private static readonly HashSet<string> KnownZones = new(StringComparer.OrdinalIgnoreCase)
        {
            "Tunis", "Ariana", "La Marsa", "Sousse", "Monastir", "Sfax", "Hammamet",
            "Bizerte", "Nabeul", "Gabès", "Gafsa", "Kairouan", "Tozeur", "Mahdia",
            "Kasserine", "Sidi Bouzid", "Jendouba", "El Kef", "Médenine", "Djerba",
            "Zaghouan", "Béja", "Siliana", "Tatouine", "Manouba", "Ben Arous"
        };

        private static string ExtractZone(string? adresse)
        {
            if (string.IsNullOrWhiteSpace(adresse))
            {
                return "Autre";
            }

            var parts = adresse.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                if (KnownZones.Contains(parts[i]))
                {
                    return parts[i];
                }
            }

            var fallback = parts.Length > 0 ? parts[^1] : adresse;
            return string.IsNullOrWhiteSpace(fallback) ? "Autre" : fallback;
        }
    }
}
