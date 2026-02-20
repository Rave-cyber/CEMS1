using System.Collections.Generic;
using System.Text.Json;

namespace CEMS.Models
{
    public class DriverExpenseFormViewModel
    {
        public List<string> Categories { get; set; } = new List<string>();

        public string CategoriesJson => JsonSerializer.Serialize(Categories ?? new List<string>());
    }
}
