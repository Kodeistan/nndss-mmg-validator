using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Kodeistan.Mmg.WebValidatorUI
{
    public class MessageForm
    {
        [Required]
        [StringLength(65535, ErrorMessage = "Content is too long.")]
        public string Content { get; set; }
    }
}
