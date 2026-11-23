using MySqlConnector;

public sealed partial class MySqlStore
{
    private const string TicketCategoryInsertSql="INSERT INTO ticket_categories(ticket_id,category_id,sort_order) VALUES(@ticket,@category,@position)";
    private static string TicketCategoryCheckSql(string[] names)=>"SELECT id FROM categories WHERE active=1 AND id IN ("+string.Join(",",names)+") FOR SHARE";
    private static string[] NormalizeTicketCategories(string primary,string[]? selected)
    {
        if(!Guid.TryParse(primary,out var primaryId))throw new ArgumentException("Select a valid primary category.");
        if((selected?.Length??0)>500)throw new ArgumentException("Choose at most 500 categories.");
        var ids=new List<string>{primaryId.ToString("D")};
        foreach(var value in selected??[]){
            if(!Guid.TryParse(value,out var id))throw new ArgumentException("Select valid categories.");
            var text=id.ToString("D");if(!ids.Contains(text,StringComparer.OrdinalIgnoreCase))ids.Add(text);
        }
        if(ids.Count>500)throw new ArgumentException("Choose at most 500 categories.");
        return ids.ToArray();
    }
    private static async Task SaveTicketCategories(MySqlConnection connection,MySqlTransaction tx,string ticket,string primary,string[]? selected,CancellationToken ct)
    {
        var ids=NormalizeTicketCategories(primary,selected);
        var names=ids.Select((_,i)=>"@category"+i).ToArray();
        await using var check=new MySqlCommand(TicketCategoryCheckSql(names),connection,tx);
        for(var i=0;i<ids.Length;i++)check.Parameters.AddWithValue(names[i],ids[i]);
        var count=0;await using(var reader=await check.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct))count++;
        if(count!=ids.Length)throw new ArgumentException("One or more selected categories are no longer available.");
        for(var i=0;i<ids.Length;i++)
            await FExecute(connection,tx,TicketCategoryInsertSql,ct,("ticket",ticket),("category",ids[i]),("position",i));
    }
    private static async Task ChangeStoredPrimaryCategory(MySqlConnection connection,MySqlTransaction tx,string ticket,string oldCategory,string newCategory,CancellationToken ct)
    {
        if(string.Equals(oldCategory,newCategory,StringComparison.OrdinalIgnoreCase))return;
        await FExecute(connection,tx,"DELETE FROM ticket_categories WHERE ticket_id=@ticket AND category_id=@old",ct,("ticket",ticket),("old",oldCategory));
        await FExecute(connection,tx,"INSERT INTO ticket_categories(ticket_id,category_id,sort_order) VALUES(@ticket,@category,0) ON DUPLICATE KEY UPDATE sort_order=0",ct,("ticket",ticket),("category",newCategory));
    }
    private static Task<List<Dictionary<string,object?>>> ReadTicketCategories(MySqlConnection connection,string ticket,CancellationToken ct)=>
        FRows(connection,null,"SELECT c.id,CASE WHEN p.name IS NULL THEN c.name ELSE CONCAT(p.name,' → ',c.name) END AS name FROM ticket_categories tc JOIN categories c ON c.id=tc.category_id LEFT JOIN categories p ON p.id=c.parent_id WHERE tc.ticket_id=@ticket ORDER BY tc.sort_order,c.name,c.id",ct,("ticket",ticket));
}
