using Microsoft.AspNetCore.Mvc;
using PoorMansTSqlFormatterLib.Parsers;
using PoorMansTSqlFormatterLib.Tokenizers;
using TSqlToPSql;
using TSqlToPSqlApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapPost("/convert", (ConvertOptions options) =>
{
    var tokenizer = new TSqlStandardTokenizer();
    var tokens = tokenizer.TokenizeSQL(options.Source, null);
    var parser = new TSqlStandardParser();
    var parsed = parser.ParseSQL(tokens);

    var converterOptions = options.Options ?? new TSqlToPsqlConverterOptions
    {
        TrailingCommas = true,
        UppercaseKeywords = false
    };

    var converter = new TSqlToPsqlConverter(converterOptions);
    var syntaxTreeTransformer = new SyntaxTreeTransformer();

    syntaxTreeTransformer.TransformTree(parsed);
    var output = converter.FormatSQLTree(parsed);

    return new { output };
})
.WithName("Convert");

app.Run();

