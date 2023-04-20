using BeatLeader_Server.Enums;

namespace BeatLeader_Server.Utils;

public static class GeneralExtensions
{
    public static Order Reverse(this Order order) => order == Order.Desc ? Order.Asc : Order.Desc;
}