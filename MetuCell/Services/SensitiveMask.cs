using System;

namespace MetuCell.Services
{
    
    //exp: centralised masking helpers for sensitive identifiers that are stored
    // in plaintext for relational reasons (TRNID is a primary key; PUK is a
    // short SIM-unlock code that fits in VARCHAR(20)). We cannot encrypt
    // them at rest without schema changes, so we minimise their exposure
    // at the application boundary by displaying masked forms by default
    // and revealing only when the operator explicitly asks.

    public static class SensitiveMask
    {
        // 11122233344 -> 111******44   (first 3 + last 2)
        public static string MaskTrn(string trn)
        {
            if (string.IsNullOrEmpty(trn)) return "";
            if (trn.Length <= 5) return new string('*', trn.Length);
            return trn.Substring(0, 3) + new string('*', trn.Length - 5) + trn.Substring(trn.Length - 2);
        }

        // 88881111 -> ****1111   (only the last 4 digits visible)
        public static string MaskPuk(string puk)
        {
            if (string.IsNullOrEmpty(puk)) return "";
            if (puk.Length <= 4) return new string('*', puk.Length);
            return new string('*', puk.Length - 4) + puk.Substring(puk.Length - 4);
        }
    }
}
