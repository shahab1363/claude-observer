// Disambiguate Timer when WinForms is referenced (Windows builds).
// System.Threading.Timer is the one used throughout the codebase.
global using Timer = System.Threading.Timer;
