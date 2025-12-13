using System;
using System.Collections.Generic;
using Assessment.Junior;
using Xunit;

namespace CodeAssessment.Tests.Template 
{
    public class CalculateTotalWithVatTests
    {
        private static List<OrderLine> MakeLines(
            params (string product, int quantity, decimal unitPrice)[] lines)
        {
            var list = new List<OrderLine>();
            foreach (var l in lines)
            {
                list.Add(new OrderLine
                {
                    ProductName = l.product,
                    Quantity = l.quantity,
                    UnitPrice = l.unitPrice
                });
            }
            return list;
        }

        [Fact]
        public void EmptyList_ReturnsZero()
        {
            // Arrange
            var lines = new List<OrderLine>();

            // Act
            var total = Program.CalculateTotalWithVat(lines);

            // Assert
            Assert.Equal(0m, total);
        }

        [Fact]
        public void NullList_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(
                () => Program.CalculateTotalWithVat(null!)
            );
        }

        [Fact]
        public void NegativeOrZeroQuantity_ThrowsArgumentException()
        {
            // Arrange
            var lines = MakeLines(
                ("C# in Depth", 0, 39.95m) // quantity = 0 → fout
            );

            // Act + Assert
            var ex = Assert.Throws<ArgumentException>(
                () => Program.CalculateTotalWithVat(lines)
            );

            Assert.Contains("Quantity must be > 0", ex.Message);
        }

        [Fact]
        public void NegativeUnitPrice_ThrowsArgumentException()
        {
            // Arrange
            var lines = MakeLines(
                ("C# in Depth", 1, -10m)
            );

            // Act + Assert
            var ex = Assert.Throws<ArgumentException>(
                () => Program.CalculateTotalWithVat(lines)
            );

            Assert.Contains("UnitPrice must be >= 0", ex.Message);
        }

        [Fact]
        public void NullOrderLine_IsIgnored()
        {
            // Arrange
            var lines = new List<OrderLine?>
            {
                new OrderLine { ProductName = "A", Quantity = 1, UnitPrice = 10m },
                null,
                new OrderLine { ProductName = "B", Quantity = 2, UnitPrice = 5m }
            }! as List<OrderLine>; // we weten dat dit runtime klopt

            // Act
            var total = Program.CalculateTotalWithVat(lines);

            // subtotal = 1*10 + 2*5 = 20 → *1.21 = 24.20
            decimal subtotal = 1 * 10m + 2 * 5m;
            decimal expected = subtotal * 1.21m;

            Assert.Equal(expected, total);
        }

        [Fact]
        public void MultipleLines_ComputesCorrectTotal()
        {
            // Arrange: jouw voorbeeld uit Main()
            var lines = MakeLines(
                ("C# in Depth", 2, 39.95m),
                ("Clean Code", 1, 34.50m)
            );

            // Act
            var total = Program.CalculateTotalWithVat(lines);

            // Assert
            decimal subtotal = 2 * 39.95m + 1 * 34.50m;
            decimal expected = subtotal * 1.21m; // 21% btw

            Assert.Equal(expected, total);
        }
    }
}
