namespace FontParser.Tables.Name
{
    public class NameIdTranslator
    {
        public static string Translate(ushort nameId)
        {
            return nameId switch
            {
                0 => "Copyright Notice",
                1 => "Family",
                2 => "Subfamily",
                3 => "Unique Identifier",
                4 => "Full Name",
                5 => "Version",
                6 => "PostScript Name",
                7 => "Trademark",
                8 => "Manufacturer",
                9 => "Designer",
                10 => "Description",
                11 => "URL Vendor",
                12 => "URL Designer",
                13 => "License",
                14 => "License URL",
                15 => "Reserved",
                16 => "Preferred Family",
                17 => "Preferred Subfamily",
                18 => "Compatible Full",
                19 => "Sample Text",
                20 => "PostScript CID",
                21 => "WWS Family",
                22 => "WWS Subfamily",
                23 => "Light Background Palette",
                24 => "Dark Background Palette",
                25 => "Variations PostScript Name Prefix",
                _ => "Unknown"
            };
        }
    }
}