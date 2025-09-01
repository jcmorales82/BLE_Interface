using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BLE_Interface.Models
{
    public class IMUSample
    {
        public short AccX { get; set; }
        public short AccY { get; set; }
        public short AccZ { get; set; }
        public short GyrX { get; set; }
        public short GyrY { get; set; }
        public short GyrZ { get; set; }
    }
}