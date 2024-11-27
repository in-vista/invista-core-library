using System.Threading.Tasks;
using GeeksCoreLibrary.Modules.PostalServices.NeDistri.Models;

namespace GeeksCoreLibrary.Modules.PostalServices.NeDistri.Interfaces;

public interface INeDistriService
{
    /// <summary>
    /// Created orders of the given order ids, gets the labels and adds them to wiser_item_file linked to the order
    /// </summary>
    /// <param name="encryptedOrderIds">Comma seperated list of encrypted order ids</param>
    /// <param name="labelType">The type of the label. This mostly depends on the size of the package</param>
    /// <param name="colliAmount">The colli amount</param>
    /// <param name="userCode">Optional userCode that can be used if a login has multiple users attached</param>
    /// <param name="orderType">The order type. This can be a shipment or return shipment</param>
    /// <returns>End user feedback detailing the result of generating the labels</returns>
    Task<string> GenerateShippingLabelAsync(string encryptedOrderIds, string labelType, int colliAmount, int? userCode,
        OrderType orderType);
}