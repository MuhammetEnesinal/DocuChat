namespace DocuChat.Infrastructure.Services.Documents.Parsing.Models;

public enum BlockType
{
    Paragraph,
    Table,
    List,
    Quote,
    Code,
    Math,            // LaTeX math block: $$...$$
    Definition,      // Definition list (term: definition)
    Figure,          // Figure with caption (^^^ ... ^^^)
    Alert,           // GitHub-style alert: > [!NOTE]
    Footnote,        // Footnote content [^1]: ...
    YamlFrontMatter, // YAML metadata: --- key: val ---
    ThematicBreak    // Horizontal rule: --- *** ___
}
