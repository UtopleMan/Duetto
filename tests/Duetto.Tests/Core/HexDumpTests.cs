using Duetto.Core.Preview;

namespace Duetto.Tests.Core;

public class HexDumpTests
{
    [Fact]
    public void Full_row_matches_the_canonical_layout()
    {
        byte[] bytes =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        ];

        var rows = HexDump.Format(bytes, 0);

        Assert.Equal(
            "00000000  89 50 4E 47 0D 0A 1A 0A  00 00 00 0D 49 48 44 52  |.PNG........IHDR|",
            Assert.Single(rows));
    }

    [Fact]
    public void Partial_row_pads_the_hex_columns()
    {
        var rows = HexDump.Format("abc"u8, 0);

        Assert.Equal(
            "00000000  61 62 63                                          |abc|",
            Assert.Single(rows));
    }

    [Fact]
    public void Rows_advance_by_sixteen_bytes_from_the_start_offset()
    {
        var rows = HexDump.Format(new byte[33], 0x1000);

        Assert.Equal(3, rows.Count);
        Assert.StartsWith("00001000  ", rows[0]);
        Assert.StartsWith("00001010  ", rows[1]);
        Assert.StartsWith("00001020  ", rows[2]);
    }

    [Fact]
    public void Empty_input_produces_no_rows() =>
        Assert.Empty(HexDump.Format([], 0));
}
