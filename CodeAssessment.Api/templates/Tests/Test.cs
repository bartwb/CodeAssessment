using System;
using Assessment.Ciratum;
using Xunit;

namespace CodeAssessment.Tests.Template
{
    public class ObjectMapperTests
    {
        // Extra types uitsluitend voor tests (type-mismatch scenarios)
        private class SourceWithStringAge
        {
            public string FirstName { get; set; } = "X";
            public string LastName { get; set; } = "Y";
            public string Age { get; set; } = "28"; // mismatch: string vs int
        }

        private class DestWithIntAgeAndDefaultRole
        {
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public int Age { get; set; }
            public string Role { get; set; } = "User"; // moet default blijven bij mismatch / ontbrekend
        }

        [Fact]
        public void NullSource_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() =>
                ObjectMapper.Map<UserDto, User>(null!)
            );
        }

        [Fact]
        public void MatchingProperties_AreCopiedToDestination()
        {
            // Arrange
            var dto = new UserDto
            {
                FirstName = "Ada",
                LastName = "Lovelace",
                Age = 28
            };

            // Act
            var user = ObjectMapper.Map<UserDto, User>(dto);

            // Assert
            Assert.Equal("Ada", user.FirstName);
            Assert.Equal("Lovelace", user.LastName);
            Assert.Equal(28, user.Age);
        }

        [Fact]
        public void DestinationOnlyProperty_RemainsDefaultValue()
        {
            // Arrange
            var dto = new UserDto
            {
                FirstName = "Ada",
                LastName = "Lovelace",
                Age = 28
            };

            // Act
            var user = ObjectMapper.Map<UserDto, User>(dto);

            // Assert
            // Role bestaat niet op DTO → moet "User" blijven (ongemoeid laten)
            Assert.Equal("User", user.Role);
        }

        [Fact]
        public void MissingSourceProperty_DoesNotThrow_AndLeavesDestinationUnchanged()
        {
            // Arrange
            // We mappen van UserDto naar een dest-type met extra property (Role).
            var dto = new UserDto { FirstName = "Ada", LastName = "Lovelace", Age = 28 };

            // Act
            var dest = ObjectMapper.Map<UserDto, DestWithIntAgeAndDefaultRole>(dto);

            // Assert
            Assert.Equal("Ada", dest.FirstName);
            Assert.Equal("Lovelace", dest.LastName);
            Assert.Equal(28, dest.Age);
            Assert.Equal("User", dest.Role); // extra dest property blijft default
        }

        [Fact]
        public void TypeMismatch_DoesNotThrow_AndLeavesDestinationDefault()
        {
            // Arrange
            // Source.Age is string, Dest.Age is int → mismatch → dest.Age moet default blijven en geen exception
            var src = new SourceWithStringAge
            {
                FirstName = "Ada",
                LastName = "Lovelace",
                Age = "28"
            };

            // Act
            var dest = ObjectMapper.Map<SourceWithStringAge, DestWithIntAgeAndDefaultRole>(src);

            // Assert
            // Namen matchen en zijn compatibel → wel gekopieerd
            Assert.Equal("Ada", dest.FirstName);
            Assert.Equal("Lovelace", dest.LastName);

            // Age mismatch → blijft default(int) = 0
            Assert.Equal(0, dest.Age);

            // Role bestaat niet op source → blijft default
            Assert.Equal("User", dest.Role);
        }

        [Fact]
        public void ReturnsNewInstance_EachCall()
        {
            // Arrange
            var dto = new UserDto { FirstName = "Ada", LastName = "Lovelace", Age = 28 };

            // Act
            var a = ObjectMapper.Map<UserDto, User>(dto);
            var b = ObjectMapper.Map<UserDto, User>(dto);

            // Assert
            Assert.NotSame(a, b); // “pure” mapping: nieuwe instance per call
        }
    }
}
