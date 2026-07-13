// This app mixes WPF and WinForms (for the tray icon), so several type names collide.
// These aliases pick the intended type project-wide. WPF wins for UI geometry; WinForms is
// referenced explicitly where needed (tray, screens, cursor).
global using Point = System.Windows.Point;
