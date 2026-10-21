#version 450 core

// Writes which object covers each pixel; used for picking objects with the mouse.
uniform uint u_ObjectId;

layout(location = 0) out uint o_ObjectId;

void main()
{
    o_ObjectId = u_ObjectId;
}
