using Kodeistan.Mmg.Model;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kodeistan.Mmg.Services
{
    public interface IMmgService
    {
        MessageMappingGuide Get(string profileIdentifier);
    }
}
