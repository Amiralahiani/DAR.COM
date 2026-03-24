using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RealEstateAdmin.Models;

namespace RealEstateAdmin.Services
{
    public class PdfExportService
    {
        public byte[] GenerateBiensPdf(IEnumerable<BienImmobilier> biens)
        {
            var rows = biens.ToList();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(24);
                    page.Size(PageSizes.A4.Landscape());
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header()
                        .Text($"Biens immobiliers - {DateTime.Now:dd/MM/yyyy HH:mm}")
                        .Bold()
                        .FontSize(14);

                    page.Content().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(30);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(2);
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(70);
                            columns.ConstantColumn(70);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("ID");
                            header.Cell().Element(HeaderCell).Text("Titre");
                            header.Cell().Element(HeaderCell).Text("Prix");
                            header.Cell().Element(HeaderCell).Text("Adresse");
                            header.Cell().Element(HeaderCell).Text("Type");
                            header.Cell().Element(HeaderCell).Text("Statut");
                            header.Cell().Element(HeaderCell).Text("Publication");
                            header.Cell().Element(HeaderCell).Text("Propriétaire");
                        });

                        foreach (var item in rows)
                        {
                            table.Cell().Element(BodyCell).Text(item.Id.ToString());
                            table.Cell().Element(BodyCell).Text(item.Titre);
                            table.Cell().Element(BodyCell).Text($"{item.Prix:N2} DT");
                            table.Cell().Element(BodyCell).Text(item.Adresse ?? "-");
                            table.Cell().Element(BodyCell).Text(item.TypeTransaction);
                            table.Cell().Element(BodyCell).Text(item.StatutCommercial);
                            table.Cell().Element(BodyCell).Text(item.PublicationStatus);
                            table.Cell().Element(BodyCell).Text(item.User?.Nom ?? item.User?.UserName ?? "-");
                        }
                    });
                });
            }).GeneratePdf();
        }

        public byte[] GenerateMessagesPdf(IEnumerable<Message> messages)
        {
            var rows = messages.ToList();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(24);
                    page.Size(PageSizes.A4.Landscape());
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header()
                        .Text($"Messages - {DateTime.Now:dd/MM/yyyy HH:mm}")
                        .Bold()
                        .FontSize(14);

                    page.Content().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(30);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1);
                            columns.ConstantColumn(110);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("ID");
                            header.Cell().Element(HeaderCell).Text("Nom");
                            header.Cell().Element(HeaderCell).Text("Email");
                            header.Cell().Element(HeaderCell).Text("Sujet");
                            header.Cell().Element(HeaderCell).Text("Statut");
                            header.Cell().Element(HeaderCell).Text("Date");
                        });

                        foreach (var item in rows)
                        {
                            table.Cell().Element(BodyCell).Text(item.Id.ToString());
                            table.Cell().Element(BodyCell).Text(item.NomUtilisateur ?? "-");
                            table.Cell().Element(BodyCell).Text(item.Email ?? "-");
                            table.Cell().Element(BodyCell).Text(item.Sujet ?? "-");
                            table.Cell().Element(BodyCell).Text(item.Statut);
                            table.Cell().Element(BodyCell).Text(item.DateCreation.ToString("dd/MM/yyyy HH:mm"));
                        }
                    });
                });
            }).GeneratePdf();
        }

        public byte[] GenerateSalesPdf(IEnumerable<SaleTransaction> sales)
        {
            var rows = sales.ToList();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(24);
                    page.Size(PageSizes.A4.Landscape());
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header()
                        .Text($"Transactions de vente - {DateTime.Now:dd/MM/yyyy HH:mm}")
                        .Bold()
                        .FontSize(14);

                    page.Content().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(30);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                            columns.ConstantColumn(110);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("ID");
                            header.Cell().Element(HeaderCell).Text("Bien");
                            header.Cell().Element(HeaderCell).Text("Montant");
                            header.Cell().Element(HeaderCell).Text("Paiement");
                            header.Cell().Element(HeaderCell).Text("Statut paiement");
                            header.Cell().Element(HeaderCell).Text("Acheteur");
                            header.Cell().Element(HeaderCell).Text("Date");
                        });

                        foreach (var item in rows)
                        {
                            table.Cell().Element(BodyCell).Text(item.Id.ToString());
                            table.Cell().Element(BodyCell).Text(item.BienImmobilier?.Titre ?? "-");
                            table.Cell().Element(BodyCell).Text($"{item.Amount:N2} DT");
                            table.Cell().Element(BodyCell).Text(item.PaymentMethod);
                            table.Cell().Element(BodyCell).Text(item.PaymentStatus);
                            table.Cell().Element(BodyCell).Text(item.Buyer?.Nom ?? item.Buyer?.UserName ?? "-");
                            table.Cell().Element(BodyCell).Text(item.CreatedAt.ToString("dd/MM/yyyy HH:mm"));
                        }
                    });
                });
            }).GeneratePdf();
        }

        private static IContainer HeaderCell(IContainer container)
        {
            return container
                .Background(Colors.Grey.Lighten3)
                .Border(1)
                .Padding(4)
                .DefaultTextStyle(x => x.Bold());
        }

        private static IContainer BodyCell(IContainer container)
        {
            return container
                .Border(1)
                .Padding(4);
        }
    }
}
