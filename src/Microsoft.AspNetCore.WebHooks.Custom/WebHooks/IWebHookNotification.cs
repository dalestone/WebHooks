﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.WebHooks
{
    public interface IWebHookNotification
    {
        string Action { get; }
    }
}
