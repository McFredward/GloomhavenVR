#nullable disable
using System;

namespace Assets.Script.Misc;

public class CallbackPromise : ICallbackPromise
{
	private enum EState
	{
		Pending,
		Resolved,
		Canceled
	}

	private EState state;

	private Action onDoneCallback;

	private Action onCancelCallback;

	public bool IsPending => state == EState.Pending;

	public CallbackPromise(Action doneCallback = null)
	{
		state = EState.Pending;
		onDoneCallback = doneCallback;
		onCancelCallback = null;
	}

	public void Done(Action callback, Action onCancelled = null)
	{
		if (state == EState.Resolved)
		{
			callback?.Invoke();
			return;
		}
		if (onCancelled != null)
		{
			Cancelled(onCancelled);
		}
		Action onDone = onDoneCallback;
		onDoneCallback = ((onDone == null) ? callback : ((Action)delegate
		{
			onDone();
			callback?.Invoke();
		}));
	}

	public void Cancelled(Action callback)
	{
		if (state == EState.Canceled)
		{
			callback?.Invoke();
			return;
		}
		onCancelCallback = ((onCancelCallback == null) ? callback : ((Action)delegate
		{
			onCancelCallback();
			callback?.Invoke();
		}));
	}

	public ICallbackPromise Then(Action onDone, Action onCancel = null)
	{
		CallbackPromise callbackPromise = new CallbackPromise();
		callbackPromise.Done(onDone);
		if (onCancel != null)
		{
			callbackPromise.Cancelled(onCancel);
		}
		Done(callbackPromise.Resolve);
		Cancelled(callbackPromise.Cancel);
		return callbackPromise;
	}

	public ICallbackPromise Then(Func<ICallbackPromise> action)
	{
		CallbackPromise p = new CallbackPromise();
		Then(delegate
		{
			action().Done(p.Resolve, p.Cancel);
		}, p.Cancel);
		return p;
	}

	public ICallbackPromise<T> Then<T>(Func<ICallbackPromise<T>> action)
	{
		CallbackPromise<T> p = new CallbackPromise<T>();
		Then(delegate
		{
			action().Done(p.Resolve, p.Cancel);
		}, p.Cancel);
		return p;
	}

	public void Resolve()
	{
		if (state == EState.Pending)
		{
			state = EState.Resolved;
			onDoneCallback?.Invoke();
		}
	}

	public void Cancel()
	{
		if (state == EState.Pending)
		{
			state = EState.Canceled;
			onCancelCallback?.Invoke();
		}
	}

	public static CallbackPromise Resolved()
	{
		CallbackPromise callbackPromise = new CallbackPromise();
		callbackPromise.Resolve();
		return callbackPromise;
	}

	public static CallbackPromise Cancelled()
	{
		CallbackPromise callbackPromise = new CallbackPromise();
		callbackPromise.Cancel();
		return callbackPromise;
	}

	public ICallbackPromise<T> Then<T>(Func<CallbackPromise<T>> action)
	{
		CallbackPromise<T> p = new CallbackPromise<T>();
		Then(delegate
		{
			action().Done(p.Resolve, p.Cancel);
		}, p.Cancel);
		return p;
	}
}
public class CallbackPromise<T> : ICallbackPromise<T>
{
	private enum EState
	{
		Pending,
		Resolved,
		Canceled
	}

	private EState state;

	private Action<T> onDoneCallback;

	private Action onCancelCallback;

	private T value;

	public bool IsPending => state == EState.Pending;

	public CallbackPromise()
	{
		state = EState.Pending;
		onDoneCallback = null;
		onCancelCallback = null;
	}

	public void Done(Action<T> callback, Action onCancelled = null)
	{
		if (state == EState.Resolved)
		{
			callback?.Invoke(value);
			return;
		}
		if (onCancelled != null)
		{
			Cancelled(onCancelled);
		}
		onDoneCallback = ((onDoneCallback == null) ? callback : ((Action<T>)delegate(T val)
		{
			onDoneCallback(val);
			callback?.Invoke(val);
		}));
	}

	public void Cancelled(Action callback)
	{
		if (state == EState.Canceled)
		{
			callback?.Invoke();
			return;
		}
		onCancelCallback = ((onCancelCallback == null) ? callback : ((Action)delegate
		{
			onCancelCallback();
			callback?.Invoke();
		}));
	}

	public ICallbackPromise<T> Then(Action<T> onDone, Action onCancel = null)
	{
		CallbackPromise<T> callbackPromise = new CallbackPromise<T>();
		callbackPromise.Done(onDone);
		if (onCancel != null)
		{
			callbackPromise.Cancelled(onCancel);
		}
		Done(callbackPromise.Resolve);
		Cancelled(callbackPromise.Cancel);
		return callbackPromise;
	}

	public ICallbackPromise<T> Then(Func<T, ICallbackPromise<T>> action)
	{
		CallbackPromise<T> p = new CallbackPromise<T>();
		Then(delegate(T val)
		{
			action(val).Done(p.Resolve, p.Cancel);
		}, p.Cancel);
		return p;
	}

	public ICallbackPromise Then(Func<T, ICallbackPromise> action)
	{
		CallbackPromise p = new CallbackPromise();
		Then(delegate(T val)
		{
			action(val).Done(p.Resolve, p.Cancel);
		}, p.Cancel);
		return p;
	}

	public void Resolve(T value)
	{
		if (state == EState.Pending)
		{
			this.value = value;
			state = EState.Resolved;
			onDoneCallback?.Invoke(value);
		}
	}

	public void Cancel()
	{
		if (state == EState.Pending)
		{
			state = EState.Canceled;
			onCancelCallback?.Invoke();
		}
	}

	public static CallbackPromise<T> Resolved(T value)
	{
		CallbackPromise<T> callbackPromise = new CallbackPromise<T>();
		callbackPromise.Resolve(value);
		return callbackPromise;
	}
}



public interface ICallbackPromise
{
	void Done(Action callback, Action onCancelled = null);

	void Cancelled(Action callback);

	ICallbackPromise Then(Action onDone, Action onCancel = null);

	ICallbackPromise Then(Func<ICallbackPromise> action);

	ICallbackPromise<T> Then<T>(Func<ICallbackPromise<T>> action);
}
public interface ICallbackPromise<T>
{
	void Done(Action<T> callback, Action onCancelled = null);

	void Cancelled(Action callback);

	ICallbackPromise<T> Then(Action<T> onDone, Action onCancel = null);

	ICallbackPromise<T> Then(Func<T, ICallbackPromise<T>> action);

	ICallbackPromise Then(Func<T, ICallbackPromise> action);
}
