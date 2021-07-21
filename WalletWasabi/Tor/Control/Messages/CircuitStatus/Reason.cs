namespace WalletWasabi.Tor.Control.Messages.CircuitStatus
{
	public enum Reason
	{
		NONE,
		TORPROTOCOL,
		INTERNAL,
		REQUESTED,
		HIBERNATING,
		RESOURCELIMIT,
		CONNECTFAILED,
		OR_IDENTITY,
		OR_CONN_CLOSED,
		TIMEOUT,
		FINISHED,
		DESTROYED,
		NOPATH,
		NOSUCHSERVICE,
		MEASUREMENT_EXPIRED,

		/// <summary>Reserved for unknown values</summary>
		UNKNOWN
	}
}
