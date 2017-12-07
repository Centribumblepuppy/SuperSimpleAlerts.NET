using System;
using System.Collections.Generic;
using System.Text;

namespace SuperSimpleAlertsNet
{
    public interface IStatefulness
    {
        bool ShouldDeduplicate(string alertCode);
        void SetAlertWasSent(string alertCode, TimeSpan deduplicationPeriod);
    }
}
