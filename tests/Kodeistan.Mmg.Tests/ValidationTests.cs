using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Kodeistan.Mmg.Services;
using Kodeistan.Mmg.Model;
using Kodeistan.Mmg;
using Kodeistan.Mmg.Tests;
using System.IO;

namespace Kodeistan.Mmg.Tests
{
    public class ValidationTests : IClassFixture<ValidatorFixture>
    {
        ValidatorFixture _fixture;

        public ValidationTests(ValidatorFixture fixture)
        {
            this._fixture = fixture;
        }

        [Fact]
        public void Finds_Vocabulary_Errors()
        {
            
        }
    }
}
