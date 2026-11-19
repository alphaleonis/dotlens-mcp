namespace CoreTestProject.Lookup;

// Each record's positional parameter declares a parameter and the property that the record synthesizes for it.

public sealed record PositionalBadge(int BadgeNumber, string BadgeHolder);

public record struct PositionalGauge(double GaugeReading);

public readonly record struct PositionalMeter(int MeterTicks);

public record PositionalEnvelope<TPayload>(TPayload EnvelopePayload);

public static class PositionalOuter
{
   public record PositionalNested(string NestedLabel);
}

public partial record PositionalTally(int TallyCount);

public partial record PositionalTally
{
   public int DoubledTally => TallyCount * 2;
}

public record PositionalSquare(int SquareEdge)
{
   public int SquareArea { get; } = SquareEdge * SquareEdge;
}

// Each of these positional parameters declares only the parameter.

public record PositionalExplicit(int ExplicitCode)
{
   public int ExplicitCode { get; init; } = ExplicitCode;
}

public record PositionalBaseTicket(int TicketCode);

public record PositionalDerivedTicket(int TicketCode, int TicketSeat) : PositionalBaseTicket(TicketCode);

public class PositionalPrimaryClass(int primaryLength)
{
   public int ReadLength() => primaryLength;
}

public struct PositionalPrimaryStruct(int primaryDepth)
{
   public int ReadDepth() => primaryDepth;
}

public static class PositionalRecordReader
{
   public static string ReadAll(
      PositionalBadge badge,
      PositionalGauge gauge,
      PositionalMeter meter,
      PositionalEnvelope<string> envelope,
      PositionalOuter.PositionalNested nested,
      PositionalTally tally,
      PositionalExplicit explicitCode,
      PositionalDerivedTicket ticket,
      PositionalSquare square)
   {
      return $"{badge.BadgeNumber} {badge.BadgeHolder} {gauge.GaugeReading} {meter.MeterTicks}"
         + $" {envelope.EnvelopePayload} {nested.NestedLabel} {tally.TallyCount} {tally.DoubledTally}"
         + $" {explicitCode.ExplicitCode} {ticket.TicketCode} {ticket.TicketSeat} {square.SquareEdge} {square.SquareArea}";
   }
}
