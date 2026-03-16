namespace DeskManager.Models;

public class AppConfig
{
    public List<SpaceData> Spaces { get; set; } = [];
    public string ActiveSpaceId { get; set; } = "";
    public ThemeConfig Theme { get; set; } = new();
}

public class SpaceData
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Space 1";
    public List<GridData> Grids { get; set; } = [];
    public List<FenceData> Fences { get; set; } = [];
}

public class ThemeConfig
{
    public string GridBackground { get; set; } = "#D01A1A2E";
    public string TitleBarColor   { get; set; } = "#BB0F3460";
    public string BorderColor     { get; set; } = "#7037474E";
    public string TextColor       { get; set; } = "#EEE0E0E0";

}

public class GridData
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Grid";
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 220;
    public double Height { get; set; } = 200;
    public bool Collapsed { get; set; } = false;
    public string? FolderPath { get; set; }
    public List<IconItemData> Items { get; set; } = [];
    
    // For tabbed/grouped grids
    public List<string> ChildGridIds { get; set; } = [];
    public string? ParentGridId { get; set; }
}

public class FenceData
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Fence";
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 220;
    public double Height { get; set; } = 200;
    public bool Collapsed { get; set; } = false;
    public string? FolderPath { get; set; }
    public List<IconItemData> Items { get; set; } = [];
}

public class IconItemData
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? OriginalPath { get; set; } // Original location before moving to grid storage
    public double? DesktopX { get; set; } // Desktop screen position X
    public double? DesktopY { get; set; } // Desktop screen position Y
}
