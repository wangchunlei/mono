//
// System.Delegate.cs
//
// Authors:
//   Miguel de Icaza (miguel@ximian.com)
//   Daniel Stodden (stodden@in.tum.de)
//   Dietmar Maurer (dietmar@ximian.com)
//
// (C) Ximian, Inc.  http://www.ximian.com
//

//
// Copyright (C) 2004 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
	/* Contains the rarely used fields of Delegate */
	class DelegateData {
		public Type target_type;
		public string method_name;
	}

#if NET_1_1
	[ClassInterface (ClassInterfaceType.AutoDual)]
#endif
#if NET_2_0
	[System.Runtime.InteropServices.ComVisible (true)]
	[Serializable]
#endif
	public abstract class Delegate : ICloneable, ISerializable
	{
		#region Sync with object-internals.h
#pragma warning disable 169, 414, 649
		private IntPtr method_ptr;
		private IntPtr invoke_impl;
		private object m_target;
		private IntPtr method;
		private IntPtr delegate_trampoline;
		private IntPtr method_code;
		private MethodInfo method_info;

		// Keep a ref of the MethodInfo passed to CreateDelegate.
		// Used to keep DynamicMethods alive.
		private MethodInfo original_method_info;

		private DelegateData data;
#pragma warning restore 169, 414, 649
		#endregion

		protected Delegate (object target, string method)
		{
			if (target == null)
				throw new ArgumentNullException ("target");

			if (method == null)
				throw new ArgumentNullException ("method");

			this.m_target = target;
			this.data = new DelegateData ();
			this.data.method_name = method;
		}

		protected Delegate (Type target, string method)
		{
			if (target == null)
				throw new ArgumentNullException ("target");

			if (method == null)
				throw new ArgumentNullException ("method");

			this.data = new DelegateData ();
			this.data.method_name = method;
			this.data.target_type = target;
		}

		public MethodInfo Method {
			get {
				if (method_info != null) {
					return method_info;
				} else {
					if (method != IntPtr.Zero) {
						method_info = (MethodInfo)MethodBase.GetMethodFromHandleNoGenericCheck (new RuntimeMethodHandle (method));
					}
					return method_info;
				}
			}
		}

		public object Target {
			get {
				return m_target;
			}
		}

		//
		// Methods
		//

		[MethodImplAttribute (MethodImplOptions.InternalCall)]
		internal static extern Delegate CreateDelegate_internal (Type type, object target, MethodInfo info, bool throwOnBindFailure);

		[MethodImplAttribute (MethodImplOptions.InternalCall)]
		internal extern void SetMulticastInvoke ();

#if NET_2_0
		private static bool arg_type_match (Type delArgType, Type argType) {
			bool match = delArgType == argType;

			// Delegate contravariance
			if (!match) {
				if (!argType.IsValueType && argType.IsAssignableFrom (delArgType))
					match = true;
			}

			return match;
		}
#endif		

		private static bool return_type_match (Type delReturnType, Type returnType) {
			bool returnMatch = returnType == delReturnType;

#if NET_2_0
			if (!returnMatch) {
				// Delegate covariance
				if (!returnType.IsValueType && delReturnType.IsAssignableFrom (returnType))
					returnMatch = true;
			}
#endif

			return returnMatch;
		}

#if NET_2_0
		public static Delegate CreateDelegate (Type type, object firstArgument, MethodInfo method, bool throwOnBindFailure)
#else
		internal static Delegate CreateDelegate (Type type, object target, MethodInfo method, bool throwOnBindFailure)
#endif
		{
#if NET_2_0
			// The name of the parameter changed in 2.0
			object target = firstArgument;
#endif

			if (type == null)
				throw new ArgumentNullException ("type");

			if (method == null)
				throw new ArgumentNullException ("method");

			if (!type.IsSubclassOf (typeof (MulticastDelegate)))
				throw new ArgumentException ("type is not a subclass of Multicastdelegate");
#if !NET_2_0
			if ((target == null) && !method.IsStatic) {
				if (throwOnBindFailure)
					throw new ArgumentException ("The method should be static.", "method");
				else
					return null;
			}
#endif

			MethodInfo invoke = type.GetMethod ("Invoke");

			if (!return_type_match (invoke.ReturnType, method.ReturnType))
				if (throwOnBindFailure)
					throw new ArgumentException ("method return type is incompatible");
				else
					return null;

			// FIXME: Figure out how net 1.1 works
#if NET_2_0
			ParameterInfo[] delargs = invoke.GetParameters ();
			ParameterInfo[] args = method.GetParameters ();

			bool argLengthMatch;

			if (target != null) {
				// delegate closed over target
				if (!method.IsStatic)
					// target is passed as this
					argLengthMatch = (args.Length == delargs.Length);
				else
					// target is passed as the first argument to the static method
					argLengthMatch = (args.Length == delargs.Length + 1);
			} else {
				if (!method.IsStatic)
					//
					// Net 2.0 feature. The first argument of the delegate is passed
					// as the 'this' argument to the method.
					//
					argLengthMatch = (args.Length + 1 == delargs.Length);
				else
					argLengthMatch = (args.Length == delargs.Length);
			}
			if (!argLengthMatch)
				if (throwOnBindFailure)
					throw new ArgumentException ("method argument length mismatch");
				else
					return null;

			bool argsMatch;
			if (target != null) {
				if (!method.IsStatic) {
					argsMatch = arg_type_match (target.GetType (), method.DeclaringType);
					for (int i = 0; i < args.Length; i++)
						argsMatch &= arg_type_match (delargs [i].ParameterType, args [i].ParameterType);
				} else {
					argsMatch = arg_type_match (target.GetType (), args [0].ParameterType);
					for (int i = 1; i < args.Length; i++)
						argsMatch &= arg_type_match (delargs [i - 1].ParameterType, args [i].ParameterType);					
				}
			} else {
				if (!method.IsStatic) {
					// The first argument should match this
					argsMatch = arg_type_match (delargs [0].ParameterType, method.DeclaringType);
					for (int i = 0; i < args.Length; i++)
						argsMatch &= arg_type_match (delargs [i + 1].ParameterType, args [i].ParameterType);
				} else {
					argsMatch = true;
					for (int i = 0; i < args.Length; i++)
						argsMatch &= arg_type_match (delargs [i].ParameterType, args [i].ParameterType);
				}
			}

			if (!argsMatch)
				if (throwOnBindFailure)
					throw new ArgumentException ("method arguments are incompatible");
				else
					return null;
#endif

			Delegate d = CreateDelegate_internal (type, target, method, throwOnBindFailure);
			if (d != null)
				d.original_method_info = method;
			return d;
		}

#if NET_2_0
		public static Delegate CreateDelegate (Type type, object firstArgument, MethodInfo method) {
			return CreateDelegate (type, firstArgument, method, true);
		}
#else
		internal static Delegate CreateDelegate (Type type, object target, MethodInfo method) {
			return CreateDelegate (type, target, method, true);
		}
#endif

#if NET_2_0
		public
#else
		internal
#endif
		static Delegate CreateDelegate (Type type, MethodInfo method, bool throwOnBindFailure)
		{
			return CreateDelegate (type, null, method, throwOnBindFailure);
		}

		public static Delegate CreateDelegate (Type type, MethodInfo method) {
			return CreateDelegate (type, method, true);
		}

		public static Delegate CreateDelegate (Type type, object target, string method)
		{
			return CreateDelegate(type, target, method, false);
		}

		static MethodInfo GetCandidateMethod (Type type, Type target, string method, BindingFlags bflags, bool ignoreCase, bool throwOnBindFailure)
		{
			if (type == null)
				throw new ArgumentNullException ("type");

			if (method == null)
				throw new ArgumentNullException ("method");

			if (!type.IsSubclassOf (typeof (MulticastDelegate)))
				throw new ArgumentException ("type is not subclass of MulticastDelegate.");

			MethodInfo invoke = type.GetMethod ("Invoke");
			ParameterInfo [] delargs = invoke.GetParameters ();
			Type[] delargtypes = new Type [delargs.Length];

			for (int i=0; i<delargs.Length; i++)
				delargtypes [i] = delargs [i].ParameterType;

			/* 
			 * FIXME: we should check the caller has reflection permission
			 * or if it lives in the same assembly...
			 */

			/*
			 * since we need to walk the inheritance chain anyway to
			 * find private methods, adjust the bindingflags to ignore
			 * inherited methods
			 */
			BindingFlags flags = BindingFlags.ExactBinding |
				BindingFlags.Public | BindingFlags.NonPublic |
				BindingFlags.DeclaredOnly | bflags;

			if (ignoreCase)
				flags |= BindingFlags.IgnoreCase;

			MethodInfo info = null;

			for (Type targetType = target; targetType != null; targetType = targetType.BaseType) {
				MethodInfo mi = targetType.GetMethod (method, flags,
					null, delargtypes, new ParameterModifier [0]);
				if (mi != null && return_type_match (invoke.ReturnType, mi.ReturnType)) {
					info = mi;
					break;
				}
			}

			if (info == null) {
				if (throwOnBindFailure)
					throw new ArgumentException ("Couldn't bind to method '" + method + "'.");
				else
					return null;
			}

			return info;
		}

#if NET_2_0
 		public 
#else
		internal
#endif
		static Delegate CreateDelegate (Type type, Type target, string method, bool ignoreCase, bool throwOnBindFailure)
		{
			if (target == null)
				throw new ArgumentNullException ("target");

			MethodInfo info = GetCandidateMethod (type, target, method,
				BindingFlags.Static, ignoreCase, throwOnBindFailure);
			if (info == null)
				return null;

			return CreateDelegate_internal (type, null, info, throwOnBindFailure);
		}

 		public static Delegate CreateDelegate (Type type, Type target, string method) {
			return CreateDelegate (type, target, method, false, true);
		}

#if NET_2_0
 		public static Delegate CreateDelegate (Type type, Type target, string method, bool ignoreCase) {
			return CreateDelegate (type, target, method, ignoreCase, true);
		}
#endif

#if NET_2_0
		public
#else
		internal
#endif
		static Delegate CreateDelegate (Type type, object target, string method, bool ignoreCase, bool throwOnBindFailure)
		{
			if (target == null)
				throw new ArgumentNullException ("target");

			MethodInfo info = GetCandidateMethod (type, target.GetType (), method,
				BindingFlags.Instance, ignoreCase, throwOnBindFailure);
			if (info == null)
				return null;

			return CreateDelegate_internal (type, target, info, throwOnBindFailure);
		}

		public static Delegate CreateDelegate (Type type, object target, string method, bool ignoreCase) {
			return CreateDelegate (type, target, method, ignoreCase, true);
		}

#if NET_2_0
		public object DynamicInvoke (params object[] args)
#else
		public object DynamicInvoke (object[] args)
#endif
		{
			return DynamicInvokeImpl (args);
		}

		protected virtual object DynamicInvokeImpl (object[] args)
		{
			if (Method == null) {
				Type[] mtypes = new Type [args.Length];
				for (int i = 0; i < args.Length; ++i) {
					mtypes [i] = args [i].GetType ();
				}
				method_info = m_target.GetType ().GetMethod (data.method_name, mtypes);
			}

#if NET_2_0
			if ((m_target != null) && Method.IsStatic) {
				// The delegate is bound to m_target
				if (args != null) {
					object[] newArgs = new object [args.Length + 1];
					args.CopyTo (newArgs, 1);
					newArgs [0] = m_target;
					args = newArgs;
				} else {
					args = new object [] { m_target };
				}
				return Method.Invoke (null, args);
			}
#endif

			return Method.Invoke (m_target, args);
		}

		public virtual object Clone ()
		{
			return MemberwiseClone ();
		}

		public override bool Equals (object obj)
		{
			Delegate d = obj as Delegate;
			
			if (d == null)
				return false;
			
			// Do not compare method_ptr, since it can point to a trampoline
			if (d.m_target == m_target && d.method == method) {
				if (d.data != null || data != null) {
					/* Uncommon case */
					if (d.data != null && data != null)
						return (d.data.target_type == data.target_type && d.data.method_name == data.method_name);
					else
						return false;
				}
				return true;
			}

			return false;
		}

		public override int GetHashCode ()
		{
			// FIXME: Sync with Equals above
			return (int)method_ptr;
		}

		protected virtual MethodInfo GetMethodImpl ()
		{
			return Method;
		}

		// This is from ISerializable
		public virtual void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			DelegateSerializationHolder.GetDelegateData (this, info, context);
		}

		public virtual Delegate[] GetInvocationList()
		{
			return new Delegate[] { this };
		}

		/// <symmary>
		///   Returns a new MulticastDelegate holding the
		///   concatenated invocation lists of MulticastDelegates a and b
		/// </symmary>
		public static Delegate Combine (Delegate a, Delegate b)
		{
			if (a == null) {
				if (b == null)
					return null;
				return b;
			} else 
				if (b == null)
					return a;

			if (a.GetType () != b.GetType ())
				throw new ArgumentException (Locale.GetText ("Incompatible Delegate Types."));
			
			return a.CombineImpl (b);
		}

		/// <symmary>
		///   Returns a new MulticastDelegate holding the
		///   concatenated invocation lists of an Array of MulticastDelegates
		/// </symmary>
#if NET_2_0
		[System.Runtime.InteropServices.ComVisible (true)]
		public static Delegate Combine (params Delegate[] delegates)
#else
		public static Delegate Combine (Delegate[] delegates)
#endif
		{
			if (delegates == null)
				return null;

			Delegate retval = null;

			foreach (Delegate next in delegates)
				retval = Combine (retval, next);

			return retval;
		}

		protected virtual Delegate CombineImpl (Delegate d)
		{
			throw new MulticastNotSupportedException (String.Empty);
		}

		public static Delegate Remove (Delegate source, Delegate value) 
		{
			if (source == null)
				return null;

			return source.RemoveImpl (value);
		}

		protected virtual Delegate RemoveImpl (Delegate d)
		{
			if (this.Equals (d))
				return null;

			return this;
		}
#if NET_1_1
		public static Delegate RemoveAll (Delegate source, Delegate value)
		{
			Delegate tmp = source;
			while ((source = Delegate.Remove (source, value)) != tmp)
				tmp = source;

			return tmp;
		}
#endif
		public static bool operator == (Delegate d1, Delegate d2)
		{
			if ((object)d1 == null) {
				if ((object)d2 == null)
					return true;
				return false;
			} else if ((object) d2 == null)
				return false;
			
			return d1.Equals (d2);
		}

		public static bool operator != (Delegate d1, Delegate d2)
		{
			return !(d1 == d2);
		}
	}
}
