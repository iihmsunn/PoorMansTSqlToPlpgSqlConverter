using TSqlToPSql;

namespace TSqlToPSqlApi;

public class ConvertOptions {
	public string Source { get; set; } = "";
	public TSqlToPsqlConverterOptions? Options { get; set; }
}
