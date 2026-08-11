using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Msgifly.Web.Models.ViewModels;

public class UserFormViewModel
{
    public int? Id { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Phone]
    public string? Phone { get; set; }

    [Display(Name = "Super admin (bypasses all permission checks)")]
    public bool IsAdmin { get; set; }

    [Display(Name = "Active (can log in)")]
    public bool Active { get; set; } = true;

    [Display(Name = "Role")]
    public int? RoleId { get; set; }

    /// <summary>Required on create; leave blank on edit to keep the current password.</summary>
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string? ConfirmPassword { get; set; }

    public List<SelectListItem> RoleOptions { get; set; } = [];
}
