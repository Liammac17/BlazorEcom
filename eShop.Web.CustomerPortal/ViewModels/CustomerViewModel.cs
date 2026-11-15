using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace eShop.Web.CustomerPortal.ViewModels
{
    public class CustomerViewModel
    {
        [Required(ErrorMessage = "Enter your full name.")]
        [Display(Name = "Full name")]
        [StringLength(150)]
        public string CustomerName { get; set; }

        [Required(ErrorMessage = "Enter your street address.")]
        [Display(Name = "Street address")]
        [StringLength(150)]
        public string CustomerAddress { get; set; }

        [Required(ErrorMessage = "Enter your city.")]
        [Display(Name = "City")]
        [StringLength(50)]
        public string CustomerCity { get; set; }

        [Required(ErrorMessage = "Enter your state or province.")]
        [Display(Name = "State or province")]
        [StringLength(50)]
        public string CustomerStateProvince { get; set; }

        [Required(ErrorMessage = "Enter your country.")]
        [Display(Name = "Country")]
        [StringLength(50)]
        public string CustomerCountry { get; set; }        
    }
}
