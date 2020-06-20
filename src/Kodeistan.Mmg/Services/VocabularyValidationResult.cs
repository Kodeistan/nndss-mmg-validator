using System;
using System.Collections.Generic;
using System.Text;

namespace Kodeistan.Mmg.Services
{
    public sealed class VocabularyValidationResult
    {
        /// <summary>
        /// Gets/sets whether the concept code is valid
        /// </summary>
        public bool IsCodeValid { get; private set; } = false;

        /// <summary>
        /// Gets/sets whether the description of the concept is valid
        /// </summary>
        public bool IsDescriptionValid { get; private set; } = false;

        /// <summary>
        /// Gets/sets whether the code system is valid
        /// </summary>
        public bool IsSystemValid { get; private set; } = false;

        public VocabularyValidationResult(bool isCodeValid, bool isDescriptionValid, bool isSystemValid)
        {
            IsCodeValid = isCodeValid;
            IsDescriptionValid = isDescriptionValid;
            IsSystemValid = isSystemValid;
        }
    }
}
