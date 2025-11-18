using System;
using System.Collections.Generic;
using FontParser.Tables;
using FontParser.Tables.Avar;
using FontParser.Tables.Base;
using FontParser.Tables.Bitmap.Cbdt;
using FontParser.Tables.Bitmap.Cblc;
using FontParser.Tables.Bitmap.Ebdt;
using FontParser.Tables.Bitmap.Eblc;
using FontParser.Tables.Bitmap.Ebsc;
using FontParser.Tables.Cff.Type1;
using FontParser.Tables.Cff.Type2;
using FontParser.Tables.Cmap;
using FontParser.Tables.Colr;
using FontParser.Tables.Cpal;
using FontParser.Tables.Cvar;
using FontParser.Tables.Fftm;
using FontParser.Tables.Fvar;
using FontParser.Tables.Gdef;
using FontParser.Tables.Gpos;
using FontParser.Tables.Gsub;
using FontParser.Tables.Gvar;
using FontParser.Tables.Head;
using FontParser.Tables.Hhea;
using FontParser.Tables.Hmtx;
using FontParser.Tables.Hvar;
using FontParser.Tables.Jstf;
using FontParser.Tables.Kern;
using FontParser.Tables.Math;
using FontParser.Tables.Merg;
using FontParser.Tables.Meta;
using FontParser.Tables.Mvar;
using FontParser.Tables.Name;
using FontParser.Tables.Optional;
using FontParser.Tables.Optional.Dsig;
using FontParser.Tables.Optional.Hdmx;
using FontParser.Tables.Proprietary.Aat.Bdat;
using FontParser.Tables.Proprietary.Aat.Bloc;
using FontParser.Tables.Proprietary.Aat.Kerx;
using FontParser.Tables.Proprietary.Aat.Morx;
using FontParser.Tables.Proprietary.Aat.Prop;
using FontParser.Tables.Proprietary.Aat.Zapf;
using FontParser.Tables.Proprietary.Bdf;
using FontParser.Tables.Proprietary.Pclt;
using FontParser.Tables.Proprietary.Pfed;
using FontParser.Tables.Proprietary.Tex;
using FontParser.Tables.Proprietary.Webf;
using FontParser.Tables.Stat;
using FontParser.Tables.Svg;
using FontParser.Tables.Todo.Arabic.Tsid;
using FontParser.Tables.Todo.Arabic.Tsif;
using FontParser.Tables.Todo.Arabic.Tsip;
using FontParser.Tables.Todo.Arabic.Tsis;
using FontParser.Tables.Todo.Arabic.Tsiv;
using FontParser.Tables.Todo.Graphite.Glat;
using FontParser.Tables.Todo.Graphite.Gloc;
using FontParser.Tables.Todo.Graphite.Silf;
using FontParser.Tables.Todo.Graphite.Sill;
using FontParser.Tables.Todo.Graphite.Silt;
using FontParser.Tables.Todo.Ttfa;
using FontParser.Tables.TtTables;
using FontParser.Tables.TtTables.Glyf;
using FontParser.Tables.Vdmx;
using FontParser.Tables.Vorg;

namespace FontParser
{
    // These are the tables that we know about.
    public static class Table
    {
        public static List<Type> Types = new List<Type>
        {
            typeof(CmapTable),
            typeof(HeadTable),
            typeof(HheaTable),
            typeof(MaxPTable),
            typeof(HmtxTable),
            typeof(NameTable),
            typeof(Os2Table),
            typeof(PostTable),
            typeof(CvtTable),
            typeof(FpgmTable),
            typeof(LocaTable),
            typeof(GlyphTable),
            typeof(PrepTable),
            typeof(GaspTable),
            typeof(DsigTable),
            typeof(HdmxTable),
            typeof(LtshTable),
            typeof(VheaTable),
            typeof(VmtxTable),
            typeof(GdefTable),
            typeof(VdmxTable),
            typeof(GposTable),
            typeof(GsubTable),
            typeof(Type1Table),
            typeof(Type2Table),
            typeof(MathTable),
            typeof(FftmTable),
            typeof(SvgTable),
            typeof(BaseTable),
            typeof(MorxTable),
            typeof(Tables.Proprietary.Aat.Feat.FeatTable),
            typeof(HvarTable),
            typeof(MvarTable),
            typeof(StatTable),
            typeof(FvarTable),
            typeof(GvarTable),
            typeof(AvarTable),
            typeof(CvarTable),
            typeof(PfedTable),
            typeof(TtfaTable),
            typeof(PropTable),
            typeof(EblcTable),
            typeof(EbdtTable),
            typeof(CbdtTable),
            typeof(CblcTable),
            typeof(EbscTable),
            typeof(TexTable),
            typeof(PcltTable),
            typeof(BdfTable),
            typeof(VorgTable),
            typeof(WebfTable),
            typeof(KernTable),
            typeof(MetaTable),
            typeof(JstfTable),
            typeof(MergTable),
            typeof(GlatTable),
            typeof(GlocTable),
            typeof(SilfTable),
            typeof(SillTable),
            typeof(BlocTable),
            typeof(BdatTable),
            typeof(KerxTable),
            typeof(ZapfTable),
            typeof(TsidTable),
            typeof(TsifTable),
            typeof(TsipTable),
            typeof(TsisTable),
            typeof(TsivTable),
            typeof(Tables.Todo.Graphite.Feat.FeatTable),
            typeof(CpalTable),
            typeof(ColrTable),
            typeof(SiltTable)
        };
    }
}